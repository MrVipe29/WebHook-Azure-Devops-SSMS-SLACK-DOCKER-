using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// =============================================================
// 1. YAPILANDIRMA VE SERVİSLER
// =============================================================

// appsettings.json dosyasını okumak için yapılandırmayı ekliyoruz. 
// (SQL bağlantı dizesi ve PAT gibi ayarlar için gereklidir)
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

// 404/500 HATA ÇÖZÜMÜ: Controller'ları (WebhookController) uygulamaya dahil eder.
builder.Services.AddControllers(); 

// API'yi tarayıcıda test etmek için Swagger/OpenAPI desteği
builder.Services.AddOpenApi(); 


// =============================================================
// 2. HTTP İSTEK PİPELINE'I (MIDDLEWARE)
// =============================================================

var app = builder.Build();

// Development ortamında detaylı hata mesajlarını ve Swagger'ı göster.
if (app.Environment.IsDevelopment())
{
    // Hata oluştuğunda 500.30 yerine detaylı hata mesajını gösterir.
    app.UseDeveloperExceptionPage(); 
    app.MapOpenApi(); // Swagger UI'ı /swagger adresinde etkinleştirir.
}

// Uygulama güvenliği ve yönlendirme ayarları
app.UseHttpsRedirection();
app.UseAuthorization(); 

// KRİTİK 404 ÇÖZÜMÜ: Controller'ların adreslemesini etkinleştirir.
// Bu satır, WebhookController'daki /api/webhook/sync adresini tanımasını sağlar.
app.MapControllers(); 

// Minimal API kodları (WeatherForecast gibi) kaldırılmıştır.

app.Run();