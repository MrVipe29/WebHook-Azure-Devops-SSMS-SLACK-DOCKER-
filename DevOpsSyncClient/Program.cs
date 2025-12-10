#nullable disable
using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.IO;

// =============================================================
// AYARLARIN YAPILANDIRMASI
// =============================================================

IConfiguration configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

string organization = configuration["Settings:Organization"];
string project = configuration["Settings:Project"];
string pat = configuration["Settings:PAT"];

string connectionString = configuration.GetConnectionString("DefaultConnection");
string azureWebhookUrl = configuration["Settings:AzureWebhookUrl"];
string slackWebhookUrl = configuration["Settings:SlackWebhookUrl"];

Console.WriteLine($"[{DateTime.Now}] Servis Başlatıldı (GÜVENLİ KONFİGÜRASYON).");

try
{
    Console.WriteLine(">>> Adım 1: Geçmiş Veriler Eşitleniyor...");
    await HerSeyiDahilEtVeTemizle();

    await SlackeGonder("🚀 *DevOps Botu Hazır!* (Eşitleme Tamamlandı)");

    Console.WriteLine(">>> Adım 2: Canlı Dinleme Moduna Geçiliyor... (Sessiz Mod)");

    await WebhookDinle();
}
catch (SqlException sqlex)
{
    Console.WriteLine($"KRİTİK HATA: SQL Bağlantı Hatası! Sunucuyu kontrol edin: {sqlex.Message}");
    Console.ReadLine();
}
catch (Exception ex)
{
    Console.WriteLine($"KRİTİK HATA: Genel Hata: {ex.Message}");
    Console.ReadLine();
}

// =============================================================
// GÜVENLİ JSON STRING PARSER (HER ŞEYİ YÖNETİR)
// =============================================================

string JsonToString(JsonElement el)
{
    if (el.ValueKind == JsonValueKind.Null)
        return "-";

    if (el.ValueKind == JsonValueKind.Object)
    {
        if (el.TryGetProperty("displayName", out var disp))
            return disp.GetString();

        return el.GetRawText();
    }

    if (el.ValueKind == JsonValueKind.Array)
        return el.GetRawText();

    return el.ToString();
}

// =============================================================
// METOT 1: FULL SYNC
// =============================================================

async Task HerSeyiDahilEtVeTemizle()
{
    using HttpClient client = new HttpClient();
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}")));

    var query = new { query = $"Select [System.Id] From WorkItems WHERE [System.TeamProject] = '{project}' AND [System.Id] > 0 ORDER BY [System.Id] DESC" };
    var content = new StringContent(JsonSerializer.Serialize(query), Encoding.UTF8, "application/json");

    var response = await client.PostAsync($"https://dev.azure.com/{organization}/{project}/_apis/wit/wiql?api-version=6.0", content);
    if (!response.IsSuccessStatusCode) return;

    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    List<int> azureIdList = new List<int>();

    if (doc.RootElement.TryGetProperty("workItems", out var workItems))
        foreach (var item in workItems.EnumerateArray())
            azureIdList.Add(item.GetProperty("id").GetInt32());

    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        List<int> sqlIdList = new List<int>();

        using (SqlCommand cmd = new SqlCommand("SELECT WorkItemId FROM WorkItemCurrentState", conn))
        using (SqlDataReader reader = cmd.ExecuteReader())
            while (reader.Read())
                sqlIdList.Add(reader.GetInt32(0));

        var silinecekler = sqlIdList.Except(azureIdList).ToList();

        foreach (var silinecekId in silinecekler)
        {
            SqlExec(conn, "DELETE FROM WorkItemCurrentState WHERE WorkItemId=@id", new { id = silinecekId });
            SqlExec(conn, "INSERT INTO WorkItemHistory (WorkItemId, ChangeDate, ChangedField, OldValue, NewValue) VALUES (@id, @date, 'SİLİNDİ', 'FullSync', 'Temizlendi')",
                new { id = silinecekId, date = DateTime.Now });
        }
    }

    using HttpClient clientData = new HttpClient();
    clientData.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}")));

    for (int i = 0; i < azureIdList.Count; i += 200)
    {
        var batchIds = azureIdList.Skip(i).Take(200);
        string idsStr = string.Join(",", batchIds);

        var detailResponse = await clientData.GetAsync(
            $"https://dev.azure.com/{organization}/{project}/_apis/wit/workitems?ids={idsStr}&fields=System.Id,System.Title,System.State,System.AssignedTo,System.ChangedDate,System.WorkItemType&api-version=6.0");

        if (!detailResponse.IsSuccessStatusCode) continue;

        using var detailDoc = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        var values = detailDoc.RootElement.GetProperty("value");

        using (SqlConnection conn = new SqlConnection(connectionString))
        {
            conn.Open();
            foreach (var item in values.EnumerateArray())
                SaveItemToSql(conn, item);
        }
    }

    Console.WriteLine(">>> Full Senkronizasyon Tamamlandı.");
}

// =============================================================
// METOT 2: WEBHOOK DİNLEME
// =============================================================

async Task WebhookDinle()
{
    using HttpClient client = new HttpClient();

    while (true)
    {
        try
        {
            string response = await client.GetStringAsync(azureWebhookUrl);

            if (!string.IsNullOrEmpty(response) && response != "[]")
            {
                var updates = JsonSerializer.Deserialize<List<JsonElement>>(response);

                if (updates != null && updates.Count > 0)
                {
                    Console.WriteLine($"[{DateTime.Now}] WEBHOOK: {updates.Count} yeni işlem alındı. İşleniyor...");
                    await SaveWebhookToSql(updates);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now}] [KRİTİK HATA] Webhook Dinleme Bağlantısı Koptu: {ex.Message}");
        }

        await Task.Delay(5000);
    }
}

// =============================================================
// WEBHOOK VERİSİNİ SQL'E İŞLE
// =============================================================

async Task SaveWebhookToSql(List<JsonElement> updates)
{
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();

        foreach (var root in updates)
        {
            string type = root.TryGetProperty("eventType", out var eventTypeProp) ? eventTypeProp.GetString() : "";
            if (!root.TryGetProperty("resource", out var res)) continue;

            int id = 0;
            if (res.TryGetProperty("workItemId", out var wid)) id = wid.GetInt32();
            else if (res.TryGetProperty("id", out var i)) id = i.GetInt32();

            DateTime now = DateTime.Now;

            // -------------------
            // SİLİNME
            // -------------------
            if (type == "workitem.deleted")
            {
                SqlExec(conn, "DELETE FROM WorkItemCurrentState WHERE WorkItemId=@id", new { id });
                SqlExec(conn, "INSERT INTO WorkItemHistory (WorkItemId, ChangeDate, ChangedField, OldValue, NewValue) VALUES (@id,@date,'SİLİNDİ','Webhook','Silindi')",
                    new { id, date = now });

                Console.WriteLine($"[LOG] SİLİNDİ: {id}");
                await SlackeGonder($"🗑️ *Bir İş Silindi!* \nID: {id}");
                continue;
            }

            // -------------------
            // OLUŞTURMA / GÜNCELLEME
            // -------------------
            if (type == "workitem.created" || type == "workitem.updated")
            {
                List<string> degisiklikListesi = new List<string>();

                if (type == "workitem.updated" && res.TryGetProperty("fields", out var fields))
                {
                    foreach (var f in fields.EnumerateObject())
                    {
                        string fName = f.Name;
                        if (fName.Contains("Date") || fName.Contains("Rev") || fName.Contains("Watermark")) continue;

                        string oldV = "Yok";
                        if (f.Value.TryGetProperty("oldValue", out var o))
                            oldV = JsonToString(o);

                        string newV = "Yok";
                        if (f.Value.TryGetProperty("newValue", out var n))
                            newV = JsonToString(n);

                        oldV = Temizle(oldV);
                        newV = Temizle(newV);

                        SqlExec(conn,
                            "INSERT INTO WorkItemHistory (WorkItemId,ChangeDate,ChangedField,OldValue,NewValue) VALUES (@id,@date,@f,@o,@n)",
                            new { id, date = now, f = fName, o = oldV, n = newV });

                        string alan = fName.Replace("System.", "").Replace("Microsoft.VSTS.Common.", "");
                        if (alan == "State") alan = "Durum";
                        if (alan == "AssignedTo") alan = "Atanan";
                        if (alan == "Title") alan = "Başlık";
                        if (alan == "WorkItemType") alan = "Tip";

                        degisiklikListesi.Add($"• {alan}: {oldV} ➝ {newV}");
                    }
                }
                else if (type == "workitem.created")
                {
                    SqlExec(conn,
                        "INSERT INTO WorkItemHistory (WorkItemId,ChangeDate,ChangedField,OldValue,NewValue) VALUES (@id,@date,'OLUŞTURULDU','-','YENİ')",
                        new { id, date = now });
                }

                var veri = await TazeVeriCekVeGuncelle(id, conn);

                string mesaj =
$@"🔄 *{veri.Tip} Güncellendi* 
🆔 ID: {id}
📌 Başlık: {veri.Baslik}
👤 Atanan: {veri.Atanan}
📊 Durum: {veri.Durum}

{(degisiklikListesi.Count > 0 ? "*Değişiklikler:*\n" + string.Join("\n", degisiklikListesi) : "")}";

                await SlackeGonder(mesaj);
            }
        }
    }
}

// =============================================================
// TAZE VERİ CEK
// =============================================================

async Task<(string Baslik, string Durum, string Atanan, string Tip)> TazeVeriCekVeGuncelle(int id, SqlConnection conn)
{
    string baslik = "-", durum = "-", atanan = "-", tip = "İş";

    try
    {
        using HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}")));

        var response = await client.GetAsync(
            $"https://dev.azure.com/{organization}/{project}/_apis/wit/workitems/{id}?fields=System.Id,System.Title,System.State,System.AssignedTo,System.ChangedDate,System.WorkItemType&api-version=6.0");

        if (response.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            SaveItemToSql(conn, root);

            var fields = root.GetProperty("fields");

            baslik = fields.TryGetProperty("System.Title", out var t) ? t.GetString() : "-";
            durum = fields.TryGetProperty("System.State", out var s) ? s.GetString() : "-";
            tip = fields.TryGetProperty("System.WorkItemType", out var wt) ? wt.GetString() : "İş";

            if (fields.TryGetProperty("System.AssignedTo", out var a))
            {
                if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("displayName", out var disp))
                    atanan = disp.GetString();
                else atanan = a.ToString();
            }
        }
    }
    catch { }

    return (baslik, durum, atanan, tip);
}

// =============================================================
// SQL KAYIT METOTLARI
// =============================================================

void SaveItemToSql(SqlConnection conn, JsonElement item)
{
    var fields = item.GetProperty("fields");
    int id = item.GetProperty("id").GetInt32();

    string title = fields.TryGetProperty("System.Title", out var t) ? t.GetString() : "-";
    string state = fields.TryGetProperty("System.State", out var s) ? s.GetString() : "-";
    string type = fields.TryGetProperty("System.WorkItemType", out var wt) ? wt.GetString() : "-";

    string assigned = "-";
    if (fields.TryGetProperty("System.AssignedTo", out var a))
    {
        if (a.ValueKind == JsonValueKind.Object)
            assigned = a.TryGetProperty("displayName", out var disp) ? disp.GetString() : "-";
        else assigned = a.ToString();
    }

    DateTime date = fields.TryGetProperty("System.ChangedDate", out var dt) ? dt.GetDateTime() : DateTime.Now;

    UpsertCurrent(conn, id, title, state, assigned, type, date);
}

void UpsertCurrent(SqlConnection conn, int id, string titleVal, string s, string a, string type, DateTime d)
{
    int count = (int)new SqlCommand($"SELECT COUNT(*) FROM WorkItemCurrentState WHERE WorkItemId={id}", conn).ExecuteScalar();

    string sql = (count == 0)
        ? "INSERT INTO WorkItemCurrentState (WorkItemId, Title, State, AssignedTo, Type, LastUpdated) VALUES (@id, @t, @s, @a, @type, @d)"
        : "UPDATE WorkItemCurrentState SET Title=@t, State=@s, AssignedTo=@a, Type=@type, LastUpdated=@d WHERE WorkItemId=@id";

    using var cmd = new SqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@id", id);
    cmd.Parameters.AddWithValue("@t", titleVal);
    cmd.Parameters.AddWithValue("@s", s);
    cmd.Parameters.AddWithValue("@a", a);
    cmd.Parameters.AddWithValue("@type", type);
    cmd.Parameters.AddWithValue("@d", d);
    cmd.ExecuteNonQuery();
}

string Temizle(string veri)
{
    if (string.IsNullOrEmpty(veri)) return "-";
    if (veri.Contains("<") && veri.Contains(">"))
        return veri.Split('<')[0].Trim();
    return veri;
}

// =============================================================
// SLACK
// =============================================================

async Task SlackeGonder(string mesaj)
{
    try
    {
        if (string.IsNullOrEmpty(slackWebhookUrl)) return;

        using HttpClient client = new HttpClient();
        var payload = new { text = mesaj };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        await client.PostAsync(slackWebhookUrl, content);
    }
    catch
    {
        Console.WriteLine("[HATA] Slack'e mesaj gönderilemedi.");
    }
}
// =============================================================
// SQL EXEC
// =============================================================
void SqlExec(SqlConnection conn, string sql, object param)
{
    using var cmd = new SqlCommand(sql, conn);
    foreach (var p in param.GetType().GetProperties())
        cmd.Parameters.AddWithValue("@" + p.Name, p.GetValue(param) ?? DBNull.Value);
    cmd.ExecuteNonQuery();
}