using System;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Hosting;
using Microsoft.Framework.DependencyInjection;
using Microsoft.Framework.Configuration;
using Microsoft.Dnx.Runtime;
using StopGuessing.Clients;
using StopGuessing.Controllers;
using StopGuessing.DataStructures;
using StopGuessing.Models;

namespace StopGuessing
{
    public class Startup
    {
        public Startup(IHostingEnvironment env, IApplicationEnvironment appEnv)
        {    // Setup configuration sources.

            //ConfigurationBuilder builder = new ConfigurationBuilder(appEnv.ApplicationBasePath);
            //.AddJsonFile("config.json")
            //    .AddJsonFile($"config.{env.EnvironmentName}.json", optional: true);

            if (env.IsDevelopment())
            {
                // This reads the configuration keys from the secret store.
                // For more details on using the user secret store see http://go.microsoft.com/fwlink/?LinkID=532709
                //builder.AddUserSecrets();
            }
            //builder.AddEnvironmentVariables();
        }

        // This method gets called by a runtime.
        // Use this method to add services to the container
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddOptions();

            // Configure MyOptions using config
            //services.Configure<BlockingAlgorithmOptions>(Configuration);

            // Configure MyOptions using code
            services.Configure<BlockingAlgorithmOptions>(myOptions =>
            {
                myOptions.BlockThresholdPopularPassword = myOptions.BlockThresholdPopularPassword;
            });


            services.AddMvc();
            // Uncomment the following line to add Web API services which makes it easier to port Web API 2 controllers.
            // You will also need to add the Microsoft.AspNet.Mvc.WebApiCompatShim package to the 'dependencies' section of project.json.
            // services.AddWebApiConventions();

            var hosts = new MaxWeightHashing<RemoteHost>("FIXME-uniquekeyfromconfig");
            hosts.Add("localhost", new RemoteHost { Uri = new Uri("http://localhost:35358"), IsLocalHost = true });

            // Use memory only stable store if none other is available.  FUTURE -- use azure SQL or tables
            services.AddSingleton<IStableStore, MemoryOnlyStableStore>();

            var options = new BlockingAlgorithmOptions();
            services.AddSingleton<BlockingAlgorithmOptions>(x => options);


            services.AddSingleton<IDistributedResponsibilitySet<RemoteHost>>( x => hosts);
            services.AddSingleton<UserAccountClient>();
            services.AddSingleton<LoginAttemptClient>();
            services.AddSingleton<UserAccountController>();
            services.AddSingleton<LoginAttemptController>();
        }

        // Configure is called after ConfigureServices is called.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            // Configure the HTTP request pipeline.
            app.UseStaticFiles();


            // Add MVC to the request pipeline.
            app.UseMvc();
            // Add the following route for porting Web API 2 controllers.
            // routes.MapWebApiRoute("DefaultApi", "api/{controller}/{id?}");
        }
    }
}
