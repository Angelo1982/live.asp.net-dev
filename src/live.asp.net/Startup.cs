// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Security.Claims;
using live.asp.net.Formatters;
using live.asp.net.Services;
using Microsoft.AspNet.Authentication.Cookies;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Hosting;
using Microsoft.AspNet.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.PlatformAbstractions;

namespace live.asp.net
{
    public class Startup
    {
        private readonly IHostingEnvironment _env;

        public static void Main(string[] args) => WebApplication.Run<Startup>(args);

        public Startup(IHostingEnvironment env, IApplicationEnvironment appEnv)
        {
            _env = env;

            var builder = new ConfigurationBuilder()
                .SetBasePath(appEnv.ApplicationBasePath)
                .AddJsonFile("appsettings.json")
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true);

            if (_env.IsDevelopment())
            {
                //Hierbei wird eine Konfiguration hinzugefügt, mit Sicherheitsrelevanten Daten, die von Umgebung zu Umgebung
                //unterschiedlich sein können.
                //see https://docs.asp.net/en/latest/security/app-secrets.html
                //builder.AddUserSecrets();

                //Braucht man für innerhalb von Azure. Gibt einem Telemetryinformation
                builder.AddApplicationInsightsSettings(developerMode: true);
            }

            //Braucht man um in verschiedenen Umgebungen entsprechende Umgebungsvariablen abzufragen. Defaultmässig kommt zuerst
            //das an die Reihe, was ich in appsettings.json gespeichert habe. Wenn man aber in einer anderern Umgebung Variablen
            //definiert, können diese die in appsettings.json überschreiben.
            builder.AddEnvironmentVariables();

            Configuration = builder.Build();
        }

        public IConfiguration Configuration { get; set; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<AppSettings>(Configuration.GetSection("AppSettings"));

            //Wenn ich mich nun einlogge, wird hier überprüft ob ich Admin sein darf
            services.AddAuthorization(options =>
            {
                options.AddPolicy("Admin", policyBuilder =>
                {
                    policyBuilder.RequireClaim(
                        ClaimTypes.Name,
                        Configuration["Authorization:AdminUsers"].Split(',')
                    );
                });
            });

            //Vergleichbar mit Google Analytics
            services.AddApplicationInsightsTelemetry(Configuration);

            services.AddMvc(options =>
            {
                options.OutputFormatters.Add(new iCalendarOutputFormatter());
            });

            //Selbst geschriebene Services
            services.AddScoped<IShowsService, YouTubeShowsService>();

            //Wenn ich den entsprechenden Connectionstring habe, greif auf den Azurestorage zu, ansonsten auf das Filesystem
            if (string.IsNullOrEmpty(Configuration["AppSettings:AzureStorageConnectionString"]))
            {
                services.AddSingleton<ILiveShowDetailsService, FileSystemLiveShowDetailsService>();
            }
            else
            {
                services.AddSingleton<ILiveShowDetailsService, AzureStorageLiveShowDetailsService>();
            }
        }

        /// <summary>
        /// Konfiguriert, wie auf HTTP Requests reagiert werden soll.
        /// </summary>
        /// <param name="app"></param>
        /// <param name="env"></param>
        /// <param name="loggerFactory"></param>
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            //MIDDLEWARE von hier an -->

            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            if (env.IsProduction())
            {
                app.UseApplicationInsightsRequestTelemetry();
            }

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {   
                app.UseExceptionHandler("/error");
            }

            if (env.IsProduction())
            {
                app.UseApplicationInsightsExceptionTelemetry();
            }

            app.UseIISPlatformHandler();

            app.UseStaticFiles();

            app.UseCookieAuthentication(options =>
            {
                options.AutomaticAuthenticate = true;
            });

            //Das macht es Azure (als Authentication Server) möglich die Authentication (Benutzer identifizieren)
            //an jemand anderen zu delegieren. Z.B. dass man mit Google Accoutn einloggen kann.
            app.UseOpenIdConnectAuthentication(options =>
            {
                options.AutomaticAuthenticate = true;
                options.AutomaticChallenge = true;
                //Geheim. Das ist eine Umgebungsvariable, die z.B. nur Azure gespeichert ist. Diese Id kann aber auch sonst wo herkommen. Z.B. aus dem json
                options.ClientId = Configuration["Authentication:AzureAd:ClientId"];
                options.Authority = Configuration["Authentication:AzureAd:AADInstance"] + Configuration["Authentication:AzureAd:TenantId"];
                options.PostLogoutRedirectUri = Configuration["Authentication:AzureAd:PostLogoutRedirectUri"];
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            });

            app.Use((context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/ping"))
                {
                    return context.Response.WriteAsync("pong");
                }
                return next();
            });

            app.UseMvc();
        }
    }
}
