using KitX_Website.Data;
using Toolbelt.Blazor.Extensions.DependencyInjection;

namespace KitX_Website
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorPages();
            builder.Services.AddServerSideBlazor();
            builder.Services.AddI18nText();     //  ¶àÓïÑÔ¿ò¼Ü
            //builder.Services.AddLocalization();
            builder.Services.AddSingleton<WeatherForecastService>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseStaticFiles();

            app.UseRouting();

            app.MapBlazorHub();
            app.MapFallbackToPage("/_Host");

            //app.UseRequestLocalization(new RequestLocalizationOptions()
            //    .AddSupportedCultures(new[] { "en-US", "zh-CN" })
            //    .AddSupportedUICultures(new[] { "en-US", "zh-CN" }));

            app.Run();
        }
    }
}