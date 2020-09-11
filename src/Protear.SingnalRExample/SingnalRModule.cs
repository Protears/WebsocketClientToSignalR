using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.SignalR;
using Volo.Abp.Modularity;

namespace Protear.SingnalRExample
{
    [DependsOn( typeof(AbpAspNetCoreSignalRModule),typeof(AbpAspNetCoreMvcModule))]
    public class SingnalRModule : AbpModule
    {
        private const string DefaultCorsPolicyName = "Default";

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            var configuration = context.Services.GetConfiguration();
            ConfigureCors(context, configuration);
        }
        private void ConfigureCors(ServiceConfigurationContext context, IConfiguration configuration)
        {
            context.Services.AddCors(options =>
            {
                options.AddPolicy(DefaultCorsPolicyName, builder =>
                {
                    //builder
                    //    .WithOrigins(
                    //        configuration["App:CorsOrigins"]
                    //            .Split(",", StringSplitOptions.RemoveEmptyEntries)
                    //            .Select(o => o.RemovePostFix("/"))
                    //            .ToArray()
                    //    )
                    //    .WithAbpExposedHeaders()
                    //    .SetIsOriginAllowedToAllowWildcardSubdomains()
                    //    .AllowAnyHeader()
                    //    .AllowAnyMethod()
                    //    .AllowCredentials();

                    builder.SetIsOriginAllowed(origin => true)
                    .WithAbpExposedHeaders()
                    .SetIsOriginAllowedToAllowWildcardSubdomains()
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
                });
            });
        }

        public override void OnApplicationInitialization(ApplicationInitializationContext context)
        {
            var app = context.GetApplicationBuilder();
            var env = context.GetEnvironment();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            app.UseConfiguredEndpoints(endpoints =>
            {
                endpoints.MapHub<ChatHub>("/hub/chat", options =>
                {
                    options.Transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
                    options.WebSockets.SubProtocolSelector = requestedProtocols =>
                    {
                        return requestedProtocols.Count > 0 ? requestedProtocols[0] : null;
                    };
                });
            });
        }
    }
}
