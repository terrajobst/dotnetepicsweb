using System.Security.Claims;

using Blazored.LocalStorage;

using DotNetEpicsWeb.Data;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DotNetEpicsWeb
{
    public class Startup
    {
        private readonly IWebHostEnvironment _environment;

        public Startup(IConfiguration configuration, IWebHostEnvironment environment)
        {
            Configuration = configuration;
            _environment = environment;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRazorPages();
            services.AddServerSideBlazor();
            services.AddControllers();
            services.AddHostedService<GitHubTreeManagerWarmUp>();
            services.AddSingleton<GitHubClientFactory>();
            services.AddSingleton<GitHubTreeManager>();
            services.AddSingleton<GitHubTreePersistence>();
            services.AddBlazoredLocalStorage();

            if (_environment.IsDevelopment())
            {
                services.AddSingleton<IGitHubTreeService, FakeTreeService>();
            }
            else
            {
                services.AddSingleton<IGitHubTreeService, GitHubTreeService>();
            }

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
                        options.ClaimActions.MapJsonKey(DotNetEpicsConstants.GitHubAvatarUrl, DotNetEpicsConstants.GitHubAvatarUrl);
                        options.Events.OnCreatingTicket = async context =>
                        {
                            var accessToken = context.AccessToken;
                            var orgName = DotNetEpicsConstants.ProductTeamOrg;
                            var teamName = DotNetEpicsConstants.ProductTeamSlug;
                            var userName = context.Identity.Name;
                            var isMember = await GitHubAuthHelpers.IsMemberOfTeamAsync(accessToken, orgName, teamName, userName);
                            if (isMember)
                                context.Identity.AddClaim(new Claim(context.Identity.RoleClaimType, DotNetEpicsConstants.ProductTeamRole));

                            context.Identity.AddClaim(new Claim(DotNetEpicsConstants.TokenClaim, accessToken));
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

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapBlazorHub();
                endpoints.MapDefaultControllerRoute();
                endpoints.MapFallbackToPage("/_Host");
            });
        }
    }
}
