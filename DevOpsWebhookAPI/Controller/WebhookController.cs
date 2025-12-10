using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

[Route("api/[controller]")] // Route: api/webhook
[ApiController]
public class WebhookController : ControllerBase
{
    // KRİTİK ÇÖZÜM: Gelen Webhook verilerini geçici olarak tutacak statik liste.
    // Client uygulaması buradan çekecektir.
    private static List<JsonElement> WebhookQueue = new List<JsonElement>();

    // 1. ADIM: Azure DevOps'tan verinin geldiği POST metodu
    [HttpPost("notify")] 
public IActionResult ReceiveWebhook([FromBody] JsonElement payload)
{
    // Olay tipini çek ve logla
    string eventType = payload.TryGetProperty("eventType", out var et) 
    ? et.GetString()!
    : "";
    
    // KRİTİK: Eğer event updated ise, loglayalım.
    Console.WriteLine($"[INFO] Webhook alındı. Tip: {eventType} | Kuyruk: {WebhookQueue.Count + 1}"); 

    WebhookQueue.Add(payload);
    
    return Ok(); 
}

    // 2. ADIM: C# Konsol Uygulamanızın veri çektiği GET metodu
    [HttpGet("sync")] 
    public IActionResult GetWorkItemUpdates()
    {
        // Kuyrukta hiç veri yoksa boş liste döndür
        if (WebhookQueue.Count == 0)
        {
            return Ok(new List<JsonElement>());
        }

        // Veri varsa, listeyi kopyala
        var updatesToSend = WebhookQueue.ToList();

        // KRİTİK: Veri Client'a sunulduktan sonra, listeden temizle (Tüketildi sayılır)
        WebhookQueue.Clear(); 
        
        // Veriyi Client'a gönder
        return Ok(updatesToSend);
    }
}