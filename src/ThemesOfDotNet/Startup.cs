using System;
using System.Security.Claims;

using Blazored.LocalStorage;

using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;

using ThemesOfDotNet.Data;
using ThemesOfDotNet.Middleware;

namespace ThemesOfDotNet
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRazorPages();
            services.AddApplicationInsightsTelemetry(Configuration["APPINSIGHTS_INSTRUMENTATIONKEY"]);
            services.AddServerSideBlazor()
                .AddHubOptions(options =>
                {
                    // Increase the limits to 256 kB
                    options.MaximumReceiveMessageSize = 262144;
                });
            services.AddControllers();
            services.AddHostedService<TreeServiceWarmUp>();
            services.AddSingleton<GitHubClientFactory>();
            services.AddSingleton<TreeService>();
            services.AddSingleton<AzureDevOpsTreeProvider>();
            services.AddSingleton<GitHubTreeProvider>();

            services.AddBlazoredLocalStorage();

            services.AddAuthentication(options =>
                    {
                        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    })
                    .AddCookie(options =>
                    {
                        options.LoginPath = "/signin";
                        options.LogoutPath = "/signout";
                    })
                    .AddGitHub(options =>
                    {
                        options.ClientId = Configuration["GitHubClientId"];
                        options.ClientSecret = Configuration["GitHubClientSecret"];
                        options.ClaimActions.MapJsonKey(ThemesOfDotNetConstants.GitHubAvatarUrl, ThemesOfDotNetConstants.GitHubAvatarUrl);
                        options.Events.OnCreatingTicket = async context =>
                        {
                            var accessToken = context.AccessToken;
                            var orgName = ThemesOfDotNetConstants.ProductTeamOrg;
                            var teamName = ThemesOfDotNetConstants.ProductTeamSlug;
                            var userName = context.Identity.Name;
                            var isMember = await GitHubAuthHelpers.IsMemberOfTeamAsync(accessToken, orgName, teamName, userName);
                            if (isMember)
                                context.Identity.AddClaim(new Claim(context.Identity.RoleClaimType, ThemesOfDotNetConstants.ProductTeamRole));
                        };
                    });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            // Redirect to themesof.net

            app.Use(async (context, next) =>
            {
                const string oldHost = "dotnetepics.azurewebsites.net";
                const string newHost = "themesof.net";
                var url = context.Request.GetUri();
                if (url.Host.Equals(oldHost, StringComparison.OrdinalIgnoreCase))
                {
                    var response = context.Response;
                    response.StatusCode = StatusCodes.Status301MovedPermanently;

                    var newUrl = new UriBuilder(url)
                    {
                        Host = newHost
                    }.ToString();

                    response.Headers[HeaderNames.Location] = newUrl;
                    return;
                }

                await next();
            });

            app.UseHttpsRedirection();
            app.UsePrecompressedStaticFiles();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapBlazorHub(options => 
                {
                    // Increase the limits to 256 kB
                    options.ApplicationMaxBufferSize = 262144;
                    options.TransportMaxBufferSize = 262144;
                });
                endpoints.MapDefaultControllerRoute();
                endpoints.MapFallbackToPage("/_Host");
            });
        }
    }
}
