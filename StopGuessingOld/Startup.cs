using System;
using System.Linq;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Hosting;
using StopGuessing.Interfaces;
using StopGuessing.Clients;
using StopGuessing.Controllers;
using StopGuessing.DataStructures;
using StopGuessing.Models;
using Microsoft.Data.Entity;
using Microsoft.Extensions.PlatformAbstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using StopGuessing.AccountStorage.Sql;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace StopGuessing
{
    public class Startup
    {
        public Startup(IHostingEnvironment env, IApplicationEnvironment appEnv)
        {
            // Setup configuration sources.
            var builder = new ConfigurationBuilder()
                .SetBasePath(appEnv.ApplicationBasePath)
                .AddJsonFile("appsettings.json");

            if (env.IsEnvironment("Development"))
            {
                // This will push telemetry data through Application Insights pipeline faster, allowing you to view results immediately.
                //builder.AddApplicationInsightsSettings(developerMode: true);
            }

            builder.AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; set; }

        // This method gets called by a runtime.
        // Use this method to add services to the container
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();
            // Add Application Insights data collection services to the services container.
            //services.AddApplicationInsightsTelemetry(Configuration);

            //var connection = @"Server=(localdb)\mssqllocaldb;Database=EFGetStarted.AspNet5.NewDb;Trusted_Connection=True;";
            string sqlConnectionString = Configuration["Data:ConnectionString"];

            services.AddEntityFramework()
                .AddSqlServer()
                .AddDbContext<DbUserAccountContext>(opt => opt.UseSqlServer(sqlConnectionString));

            // Uncomment the following line to add Web API services which makes it easier to port Web API 2 controllers.
            // You will also need to add the Microsoft.AspNet.Mvc.WebApiCompatShim package to the 'dependencies' section of project.json.
            // services.AddWebApiConventions();

            BlockingAlgorithmOptions options = new BlockingAlgorithmOptions();

            services.AddSingleton<BlockingAlgorithmOptions>(x => options);

            RemoteHost localHost = new RemoteHost {Uri = new Uri("http://localhost:35358")};
            services.AddSingleton<RemoteHost>(x => localHost);

            MaxWeightHashing<RemoteHost> hosts = new MaxWeightHashing<RemoteHost>(Configuration["Data:UniqueConfigurationSecretPhrase"]);
            hosts.Add("localhost", localHost);
            services.AddSingleton<IDistributedResponsibilitySet<RemoteHost>>(x => hosts);

            string cloudStorageConnectionString = Configuration["Data:StorageConnectionString"];
            CloudStorageAccount cloudStorageAccount = CloudStorageAccount.Parse(cloudStorageConnectionString);
            services.AddSingleton<CloudStorageAccount>( a => cloudStorageAccount);

            services.AddSingleton<MemoryUsageLimiter, MemoryUsageLimiter>();

            if (hosts.Count > 0)
            {
                DistributedBinomialLadderFilterClient dblfClient = new DistributedBinomialLadderFilterClient(
                    options.NumberOfVirtualNodesForDistributedBinomialLadder,
                    options.HeightOfBinomialLadder_H,
                    hosts,
                    options.PrivateConfigurationKey,
                    options.MinimumBinomialLadderFilterCacheFreshness);
                // If running as a distributed system
                services.AddSingleton<IBinomialLadderFilter, DistributedBinomialLadderFilterClient>(x => dblfClient);
                
                DistributedBinomialLadderFilterController filterController =
                    new DistributedBinomialLadderFilterController(dblfClient, options.NumberOfBitsPerShardInBinomialLadderFilter, options.PrivateConfigurationKey);
                services.AddSingleton<DistributedBinomialLadderFilterController>(x => filterController);

                //services.AddSingleton<IFrequenciesProvider<string>>(x =>
                //    new IncorrectPasswordFrequencyClient(hosts, options.NumberOfRedundantHostsToCachePasswordPopularity));
            }  else
            {
                BinomialLadderFilter localPasswordBinomialLadderFilter =
                    new BinomialLadderFilter(options.NumberOfBitsInBinomialLadderFilter_N, options.HeightOfBinomialLadder_H);
                services.AddSingleton<IBinomialLadderFilter>(x => localPasswordBinomialLadderFilter);
            }

            LoginAttemptClient<DbUserAccount> loginAttemptClient = new LoginAttemptClient<DbUserAccount>(hosts, localHost);
            services.AddSingleton<IUserAccountRepositoryFactory<DbUserAccount>, DbUserAccountRepositoryFactory>();
            services.AddSingleton<IUserAccountControllerFactory<DbUserAccount>, DbUserAccountControllerFactory>();
            //LoginAttemptController<DbUserAccount> loginAttemptController = new LoginAttemptController<DbUserAccount>( 
            //     new DbUserAccountControllerFactory(cloudStorageAccount), new DbUserAccountRepositoryFactory());
            services.AddSingleton<ILoginAttemptClient, LoginAttemptClient<DbUserAccount>>( i => loginAttemptClient );
            services.AddSingleton<ILoginAttemptController, LoginAttemptController<DbUserAccount>>();
        }

        // Configure is called after ConfigureServices is called.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.MinimumLevel = LogLevel.Information;
            //loggerFactory. .AddConsole();
            //loggerFactory.AddDebug();

            // Add the platform handler to the request pipeline.
            app.UseIISPlatformHandler();

            //// Add Application Insights to the request pipeline to track HTTP request telemetry data.
            //app.UseApplicationInsightsRequestTelemetry();

            //// Track data about exceptions from the application. Should be configured after all error handling middleware in the request pipeline.
            //app.UseApplicationInsightsExceptionTelemetry();

            // Configure the HTTP request pipeline.
            app.UseDefaultFiles();
            app.UseStaticFiles();

            // Add MVC to the request pipeline.
            app.UseMvc();
            // Add the following route for porting Web API 2 controllers.
            // routes.MapWebApiRoute("DefaultApi", "api/{controller}/{id?}");

        }
    }
}
