# DevOpsSyncClient

DevOpsSyncClient, Azure DevOps üzerindeki Work Item olaylarını (Create / Update / Delete) takip ederek  
verileri SQL Server üzerinde tutan, değişiklik geçmişini kaydeden ve Slack üzerinden bildirim gönderen  
Docker uyumlu bir senkronizasyon servisidir.

---

# 📌 Gereksinimler

## 🟥 1) Gerekli Yazılımlar

Aşağıdaki uygulamalar kurulu olmalıdır:

- **.NET 8 SDK**
- **SQL Server (SSMS ile yönetilebilir)**
- **Docker Desktop**
- **Azure DevOps hesabı**
- **Slack Workspace + Incoming Webhook**

---

# 🟥 2) SQL Server Gereksinimleri

Aşağıdaki iki tablo gereklidir.

### 📂 **WorkItemCurrentState**

| Kolon          | Tipi        | Açıklama                                |
|----------------|-------------|------------------------------------------|
| WorkItemId     | int (PK)    | Work Item ID                             |
| Title          | nvarchar    | Son başlık                               |
| State          | nvarchar    | Son durum                                |
| AssignedTo     | nvarchar    | Atanan kişi                               |
| Type           | nvarchar    | Work item tipi (Task, Bug vs.)           |
| LastUpdated    | datetime    | Son güncellenme zamanı                    |

---

### 📂 **WorkItemHistory**

| Kolon          | Tipi        | Açıklama                                   |
|----------------|-------------|---------------------------------------------|
| WorkItemId     | int         | Değişiklik yapılan ID                       |
| ChangeDate     | datetime    | Değişiklik zamanı                           |
| ChangedField   | nvarchar    | Değişen alan adı                            |
| OldValue       | nvarchar    | Eski değer                                  |
| NewValue       | nvarchar    | Yeni değer                                  |

---

# 🟥 3) Azure DevOps Gereksinimleri

Aşağıdaki bilgileri sağlamanız gerekir:

- **Organization Name**
- **Project Name**
- **Personal Access Token (PAT) → Read & Write Work Items**
- **Webhook veya API üzerinden erişim**

Webhook URL’si backend API’nizin verdiği endpoint olmalıdır  
(`https://example.com/api/webhook/sync` gibi).

---

# 🟥 4) Slack Gereksinimleri

Slack içinde:

1. Ayarlar → Apps & Integrations
2. **Incoming Webhook → Add New**
3. Kanal seç → “Webhook URL” oluştur

Bu URL, bildirimlerin gönderileceği yer olacaktır.

---

# ⚙️ Kurulum

## 1) appsettings.json dosyasını düzenleyin

Aşağıdaki örnek dosyanın içindeki kritik değerleri **kendiniz doldurmalısınız**:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=---;Database=WebHookDb;User Id=---;Password=---;TrustServerCertificate=True;"
  },
  "Settings": {
    "Organization": "",
    "Project": "",
    "PAT": "",
    "AzureWebhookUrl": "",
    "SlackWebhookUrl": ""
  }
}
⚠️ Bu bilgileri GitHub üzerinde paylaşmayın.
⚠️ Bu bilgileri GitHub üzerinde paylaşmayın.
⚠️ Bu bilgileri GitHub üzerinde paylaşmayın.
⚠️ Bu bilgileri GitHub üzerinde paylaşmayın.

🐳 Docker Üzerinde Çalıştırma
Build:
docker build -t devopssyncclient .

Run:
docker run -d --name sync devopssyncclient


Container otomatik olarak:

Azure DevOps değişikliklerini dinler

SQL Server tablolarını günceller

Slack’e mesaj gönderir

📡 Çalışma Mantığı
✔ Full Sync

Azure DevOps’tan tüm Work Item’ları çeker ve SQL’e yazar.

✔ Webhook / Polling Dinleme

Yeni bir değişiklik olduğunda bot bunu algılar ve işlem yapar:

Oluşturma → SQL’e ekler + Slack bildirimi

Güncelleme → SQL’i günceller + History tablosuna ekleme + Slack bildirimi

Silme → SQL’den kaldırır + Slack bildirimi

✔ Docker ile 7/24 çalışabilir
🔒 Güvenlik Uyarısı

Bu proje kişisel API anahtarları, bağlantı bilgileri ve güvenlik tokenları gerektirir.

Bu nedenle PAYLAŞMAK yasaktır.

Aşağıdaki durumlar kesinlikle yasaktır:

❌ Kodun yeniden dağıtılması
❌ Ticari kullanım
❌ Kopyalanması
❌ Fork edilmesi
❌ Yeni bir paylaşımda yayınlanması

Bu proje kişisel kullanım içindir.
Tüm hakları saklıdır.

📝 Lisans

Bu proje paylaşılması, çoğaltılması veya dağıtılması izin gerektiren özel bir lisanstır.
Tüm hakları saklıdır. Kullanıcılar sadece kendi ortamında çalıştırmak için kodu inceleyebilir.
Her türlü yeniden paylaşım ve dağıtım yasaktır.

📬 İletişim

Her türlü genişletme, özel entegrasyon veya geliştirme talebi için proje sahibine GitHub üzerinden ulaşabilirsiniz.
⚠️ Bu bilgileri GitHub üzerinde paylaşmayın.
