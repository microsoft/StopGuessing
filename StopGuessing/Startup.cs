using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.WindowsAzure.Storage;
using StopGuessing.AccountStorage.Sql;
using StopGuessing.Clients;
using StopGuessing.Controllers;
using StopGuessing.DataStructures;
using StopGuessing.Interfaces;
using StopGuessing.Models;

namespace StopGuessing
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.
            services.AddMvc();

            string sqlConnectionString = Configuration["Data:ConnectionString"];
            services.AddDbContext<DbUserAccountContext>(
                opt => opt.UseSqlServer(sqlConnectionString));

            // Uncomment the following line to add Web API services which makes it easier to port Web API 2 controllers.
            // You will also need to add the Microsoft.AspNet.Mvc.WebApiCompatShim package to the 'dependencies' section of project.json.
            // services.AddWebApiConventions();

            BlockingAlgorithmOptions options = new BlockingAlgorithmOptions();

            services.AddSingleton<BlockingAlgorithmOptions>(x => options);

            RemoteHost localHost = new RemoteHost { Uri = new Uri("http://localhost:35358") };
            services.AddSingleton<RemoteHost>(x => localHost);

            MaxWeightHashing<RemoteHost> hosts = new MaxWeightHashing<RemoteHost>(Configuration["Data:UniqueConfigurationSecretPhrase"]);
            hosts.Add("localhost", localHost);
            services.AddSingleton<IDistributedResponsibilitySet<RemoteHost>>(x => hosts);

            string cloudStorageConnectionString = Configuration["Data:StorageConnectionString"];
            CloudStorageAccount cloudStorageAccount = CloudStorageAccount.Parse(cloudStorageConnectionString);
            services.AddSingleton<CloudStorageAccount>(a => cloudStorageAccount);

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
            }
            else
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
            services.AddSingleton<ILoginAttemptClient, LoginAttemptClient<DbUserAccount>>(i => loginAttemptClient);
            services.AddSingleton<ILoginAttemptController, LoginAttemptController<DbUserAccount>>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            app.UseMvc();
        }
    }
}
