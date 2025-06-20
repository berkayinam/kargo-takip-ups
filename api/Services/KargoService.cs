using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Text.Json;
using System.IO;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;
using System.Threading;

namespace KargoTakip.Services
{
    public class KargoService
    {
        private readonly HttpClient _httpClient;
        private readonly string _dataFilePath;
        private List<KargoData> _kargoList;
        private readonly ILogger<KargoService> _logger;
        private readonly string _fourMeEmail;
        private readonly string _fourMePassword;

        public KargoService(ILogger<KargoService> logger, IConfiguration configuration)
        {
            _httpClient = new HttpClient();
            _logger = logger;
            _kargoList = new List<KargoData>();
            _dataFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "kargo_data.json");
            Directory.CreateDirectory(Path.GetDirectoryName(_dataFilePath));
            LoadKargoData();
            _fourMeEmail = configuration["FourMe:Email"] ?? "";
            _fourMePassword = configuration["FourMe:Password"] ?? "";
        }

        public string FourMeEmail => _fourMeEmail;
        public string FourMePassword => _fourMePassword;

        private void LoadKargoData()
        {
            try
            {
                if (File.Exists(_dataFilePath))
                {
                    var json = File.ReadAllText(_dataFilePath);
                    _kargoList = JsonSerializer.Deserialize<List<KargoData>>(json) ?? new List<KargoData>();
                    _logger.LogInformation($"Kargo verileri yüklendi. Toplam {_kargoList.Count} kargo bulundu.");
                }
                else
                {
                    _logger.LogInformation("Kargo veri dosyası bulunamadı. Yeni dosya oluşturulacak.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kargo verileri yüklenirken hata oluştu");
                _kargoList = new List<KargoData>();
            }
        }

        private void SaveKargoData()
        {
            try
            {
                var json = JsonSerializer.Serialize(_kargoList, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_dataFilePath, json);
                _logger.LogInformation($"Kargo verileri kaydedildi. Toplam {_kargoList.Count} kargo kaydedildi.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kargo verileri kaydedilirken hata oluştu");
            }
        }

        public async Task<List<KargoData>> GetAllKargos()
        {
            return await Task.FromResult(_kargoList);
        }

        public async Task<KargoData?> GetKargoByTrackingNumber(string? trackingNumber)
        {
            if (string.IsNullOrEmpty(trackingNumber))
                return null;
                
            return await Task.FromResult(_kargoList.FirstOrDefault(k => k.TrackingNumber == trackingNumber));
        }

        public async Task AddKargo(KargoData kargo)
        {
            if (kargo == null || string.IsNullOrEmpty(kargo.TrackingNumber))
                return;

            if (!_kargoList.Any(k => k.TrackingNumber == kargo.TrackingNumber))
            {
                _kargoList.Add(kargo);
                SaveKargoData();
            }
        }

        public async Task UpdateKargoStatus(string? trackingNumber, string status)
        {
            if (string.IsNullOrEmpty(trackingNumber))
                return;

            var kargo = _kargoList.FirstOrDefault(k => k.TrackingNumber == trackingNumber);
            if (kargo != null)
            {
                kargo.Status = status;
                kargo.LastUpdated = DateTime.Now;
                SaveKargoData();
            }
        }

        public async Task CheckKargoStatuses()
        {
            _logger.LogInformation("Kargo durumları kontrol ediliyor...");
            var kargolar = await GetAllKargos();
            _logger.LogInformation($"Toplam {kargolar.Count} kargo kontrol edilecek.");
            
            var semaphore = new SemaphoreSlim(2); // Aynı anda 2 sorgu
            var tasks = new List<Task>();
            
            foreach (var kargo in kargolar)
            {
                if (string.IsNullOrEmpty(kargo.TrackingNumber))
                    continue;
                
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await semaphore.WaitAsync(); // Sorgu sırası için bekle
                        
                        try
                        {
                            _logger.LogInformation($"Kargo durumu kontrol ediliyor: {kargo.TrackingNumber}");
                            var response = await _httpClient.GetStringAsync($"https://www.ups.com.tr/WaybillSorgu.aspx?Waybill={kargo.TrackingNumber}");
                            
                            // Öngörülen teslimat zamanını al
                            var ongorulenMatch = Regex.Match(response, 
                                @"<span[^>]*id=""ctl00_MainContent_Label2""[^>]*>Öngörülen Teslimat Zamanı<\/span><br\s*\/?>\s*<span[^>]*id=""ctl00_MainContent_teslimat_zamani""[^>]*>(.*?)<\/span>", 
                                RegexOptions.Singleline);
                            
                            if (ongorulenMatch.Success)
                            {
                                var ongorulen = ongorulenMatch.Groups[1].Value.Trim();
                                ongorulen = Regex.Replace(ongorulen, "<.*?>", "").Trim();
                                kargo.EstimatedDelivery = ongorulen;
                            }
                            
                            // Teslim durumunu kontrol et
                            if (response.Contains("Paketiniz teslim edilmiştir", StringComparison.OrdinalIgnoreCase))
                            {
                                kargo.Status = "Teslim Edildi";
                            }
                            else
                            {
                                kargo.Status = "Beklemede";
                            }
                            
                            kargo.LastUpdated = DateTime.Now;
                            SaveKargoData();
                            
                            _logger.LogInformation($"Kargo durumu güncellendi: {kargo.TrackingNumber} - {kargo.Status}");
                        }
                        finally
                        {
                            semaphore.Release(); // Sorgu tamamlandı, sıradaki sorguya geç
                        }
                        
                        await Task.Delay(500); // Sorgular arasında 500ms bekle (saniyede 2 sorgu için)
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Kargo durumu kontrol edilirken hata oluştu: {kargo.TrackingNumber}");
                    }
                }));
            }
            
            await Task.WhenAll(tasks);
            _logger.LogInformation("Kargo durumları kontrolü tamamlandı.");
        }

        public async Task LoadDataFrom4me(string? email, string? password)
        {
            email = string.IsNullOrEmpty(email) ? _fourMeEmail : email;
            password = string.IsNullOrEmpty(password) ? _fourMePassword : password;
            if (string.IsNullOrEmpty(email))
            {
                _logger.LogError("4me e-posta adresi eksik");
                throw new InvalidOperationException("4me e-posta adresi eksik");
            }
            if (string.IsNullOrEmpty(password))
            {
                _logger.LogError("4me şifre eksik");
                throw new InvalidOperationException("4me şifre eksik");
            }

            var options = new ChromeOptions();
            options.AddArgument("--headless");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--window-size=1920,1080");
            options.AddArgument("--disable-extensions");
            options.AddArgument("--disable-infobars");
            options.AddArgument("--remote-debugging-port=9222");
            options.AddArgument("--disable-blink-features=AutomationControlled");
            options.AddArgument("--disable-notifications");
            options.AddArgument("--disable-popup-blocking");
            options.AddArgument("--start-maximized");
            options.AddArgument("--disable-web-security");
            options.AddArgument("--allow-running-insecure-content");
            options.AddArgument("--ignore-certificate-errors");
            options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/109.0.0.0 Safari/537.36");

            var service = ChromeDriverService.CreateDefaultService();
            service.HideCommandPromptWindow = true;

            using (var driver = new ChromeDriver(service, options))
            {
                try
                {
                    driver.Navigate().GoToUrl("https://gratis-it.4me.com/inbox");
                    await Task.Delay(5000);
                    
                    var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));
                    
                    // E-posta girişi
                    var emailInput = wait.Until(d => d.FindElement(By.Id("i0116")));
                    emailInput.Clear();
                    emailInput.SendKeys(email);
                    await Task.Delay(3000);
                    
                    // İleri butonu 1
                    var ileriBtn1 = driver.FindElement(By.Id("idSIButton9"));
                    ileriBtn1.Click();
                    await Task.Delay(3000);
                    
                    // Şifre girişi
                    var passwordInput = driver.FindElement(By.Id("i0118"));
                    passwordInput.Clear();
                    passwordInput.SendKeys(password);
                    await Task.Delay(3000);
                    
                    // İleri butonu 2
                    var ileriBtn2 = driver.FindElement(By.Id("idSIButton9"));
                    ileriBtn2.Click();
                    await Task.Delay(3000);
                    
                    // İleri butonu 3
                    var ileriBtn3 = driver.FindElement(By.Id("idSIButton9"));
                    ileriBtn3.Click();
                    await Task.Delay(5000);

                    // Toplam öğe sayısını al
                    int totalItems = 0;
                    try
                    {
                        var totalItemsElement = wait.Until(d => d.FindElement(By.Id("view_counter")));
                        var totalItemsText = totalItemsElement.Text.Trim();
                        if (int.TryParse(totalItemsText.Replace(" öğe", ""), out totalItems))
                        {
                            _logger.LogInformation($"Toplam öğe sayısı bulundu: {totalItems}");
                        }
                        else
                        {
                            _logger.LogWarning($"Toplam öğe sayısı metni sayıya çevrilemedi: {totalItemsText}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Toplam öğe sayısı elementi bulunamadı: {ex.Message}");
                    }

                    var islenenTalepSayisi = 0;
                    var bulunanKargoSayisi = 0;
                    var islenenKargolar = new HashSet<string>();
                    var yeniEklenenKargolar = new List<KargoData>();

                    // Scroll yaparak talepleri dinamik olarak yükle ve işle
                    var scrollContainer = wait.Until(d => d.FindElement(By.Id("view_list_container")));
                    
                    _logger.LogInformation("Talepleri scroll yaparak yükleme ve işleme başlatılıyor...");
                    
                    int previousTaleplerCount = 0;
                    int noProgressCount = 0; // İlerleme olmayan scroll denemesi sayısı
                    const int maxNoProgress = 5; // Maksimum ilerleme olmayan deneme sayısı

                    while (islenenKargolar.Count(k => k.StartsWith("TALEP_")) < totalItems)
                    {
                        // Mevcut görünümdeki tüm talepleri al
                        var currentTalepler = driver.FindElements(By.CssSelector("div.grid-row")).ToList();
                        _logger.LogInformation($"Mevcut görünümde {currentTalepler.Count} talep bulundu.");

                        int processedInIteration = 0;

                        foreach (var talep in currentTalepler)
                        {
                            // Talep ID'sini al
                            string talepId = "";
                            try {
                                var talepIdElement = talep.FindElement(By.CssSelector("div.cell-path"));
                                var talepIdText = talepIdElement.Text.Trim();
                                talepId = Regex.Match(talepIdText, @"\d+").Value;
                            }
                            catch {
                                // Eğer ID alınamazsa bu elementi atla ve logla
                                _logger.LogWarning("Bir talep elementi için ID bulunamadı, atlanıyor.");
                                continue; // Bu elementi atla, işlenmiş sayma
                            }
                            
                            // Eğer bu talep daha önce işlenmediyse devam et
                            if (islenenKargolar.Contains("TALEP_" + talepId))
                            {
                                continue; // Zaten işlenmiş, atla
                            }

                            // Talep işlenmemiş, şimdi işle
                            try
                            {
                                // Konu kontrolü
                                string konu = "";
                                try {
                                    var konuElement = talep.FindElement(By.CssSelector("div.cell-subject span"));
                                    konu = konuElement.GetAttribute("title") ?? konuElement.Text.Trim();
                                }
                                catch {
                                    try {
                                        var konuElement = talep.FindElement(By.CssSelector("div.cell-subject"));
                                        konu = konuElement.Text.Trim();
                                    }
                                    catch {
                                        _logger.LogWarning($"Talep {talepId} için konu bulunamadı, atlanıyor.");
                                        islenenKargolar.Add("TALEP_" + talepId); // İşlendi olarak işaretle
                                        processedInIteration++;
                                        continue;
                                    }
                                }
                                
                                if (!konu.StartsWith("-"))
                                {
                                    _logger.LogInformation($"Talep {talepId} için konu '-' ile başlamıyor, atlanıyor: {konu}");
                                    islenenKargolar.Add("TALEP_" + talepId); // İşlendi olarak işaretle
                                    processedInIteration++;
                                    continue;
                                }

                                // Talebe tıkla ve detayları kontrol et
                                try {
                                    talep.Click();
                                    await Task.Delay(750); // Tıklama sonrası biraz daha bekleme süresi düşürüldü
                                }
                                catch (Exception ex) {
                                    _logger.LogError($"Talep {talepId} tıklanamadı: {ex.Message}");
                                    islenenKargolar.Add("TALEP_" + talepId); // İşlendi olarak işaretle
                                    processedInIteration++;
                                    continue;
                                }

                                // Talep detay sayfası elementlerini beklemek için WebDriverWait oluştur
                                var talepWait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

                                // Sayfanın yüklendiğini belirten bir elementi bekleyin
                                try
                                {
                                    talepWait.Until(d => d.FindElement(By.ClassName("header_bar_inner")));
                                    _logger.LogInformation($"Talep {talepId} detay sayfası yüklendi.");
                                }
                                catch (WebDriverTimeoutException)
                                {
                                    _logger.LogWarning($"Talep {talepId} detay sayfası yüklenemedi, atlanıyor.");
                                    islenenKargolar.Add("TALEP_" + talepId); // İşlendi olarak işaretle
                                    processedInIteration++;
                                    try { driver.Navigate().Back(); await Task.Delay(1000); } catch { } // Geri dönerken daha uzun bekle
                                    continue;
                                }

                                string magazaId = "";
                                string potentialPersonOrStoreName = "";

                                try
                                {
                                    // 'Talep eden' etiketini bul
                                    var talepEdenLabel = talepWait.Until(d => d.FindElement(By.XPath("//div[@class='label' and @title='Talep eden']")));
                                    var talepEdenValueElement = talepEdenLabel.FindElement(By.XPath("./following-sibling::div[@class='row-value']//span[@class='link-text']"));
                                    potentialPersonOrStoreName = talepEdenValueElement.Text.Trim();
                                    _logger.LogInformation($"Talep {talepId} için 'Talep eden' kısmından bulunan metin: {potentialPersonOrStoreName}");

                                    if (potentialPersonOrStoreName == "Ayse GORDAG" || potentialPersonOrStoreName == "Eren BESIROGLU" || potentialPersonOrStoreName == "Ahmet Hakan ERGUL")
                                    {
                                        _logger.LogInformation($"Talep {talepId} için kişi adı tespit edildi, 'Talep sahibi' kısmından mağaza aranıyor.");
                                        try
                                        {
                                            var talepSahibiLabel = driver.FindElement(By.XPath("//div[@class='label' and @title='Talep sahibi']"));
                                            var talepSahibiValueElement = talepSahibiLabel.FindElement(By.XPath("./following-sibling::div[@class='row-value']//span[@class='link-text']"));
                                            magazaId = talepSahibiValueElement.Text.Trim();
                                            _logger.LogInformation($"Talep {talepId} için 'Talep sahibi' kısmından bulunan mağaza metni: {magazaId}");
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogWarning($"Talep {talepId} için 'Talep sahibi' kısmından mağaza metni alınamadı: {ex.Message}");
                                        }
                                    }
                                    else
                                    {
                                        magazaId = potentialPersonOrStoreName;
                                        _logger.LogInformation($"Talep {talepId} için 'Talep eden' kısmından doğrudan mağaza adı alındı: {magazaId}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning($"Talep {talepId} için 'Talep eden' veya 'Talep sahibi' bilgisini alırken hata oluştu: {ex.Message}");
                                }

                                if (string.IsNullOrEmpty(magazaId))
                                {
                                    _logger.LogWarning($"Talep {talepId} için mağaza ID'si boş veya null kaldı.");
                                    islenenKargolar.Add("TALEP_" + talepId); // İşlendi olarak işaretle
                                    processedInIteration++;
                                    driver.Navigate().Back();
                                    await Task.Delay(1000); // Geri dönerken daha uzun bekle
                                    continue;
                                }

                                // Talep detaylarını kontrol et
                                var requestContent = driver.PageSource;
                                
                                // Farklı kargo numarası formatlarını dene
                                var trackingNumber = "";
                                
                                // UPS formatları
                                var upsMatches = Regex.Matches(requestContent, @"1[Zz][0-9A-Za-z]{16}");
                                if (upsMatches.Count > 0)
                                {
                                    trackingNumber = upsMatches[upsMatches.Count - 1].Value;
                                }
                                
                                // Aras Kargo formatı
                                if (string.IsNullOrEmpty(trackingNumber))
                                {
                                    var arasMatches = Regex.Matches(requestContent, @"[A-Z]{2}\d{9}");
                                    if (arasMatches.Count > 0)
                                    {
                                        trackingNumber = arasMatches[arasMatches.Count - 1].Value;
                                    }
                                }
                                
                                // Yurtiçi Kargo formatı
                                if (string.IsNullOrEmpty(trackingNumber))
                                {
                                    var yurticiMatches = Regex.Matches(requestContent, @"\d{13}");
                                    if (yurticiMatches.Count > 0)
                                    {
                                        trackingNumber = yurticiMatches[yurticiMatches.Count - 1].Value;
                                    }
                                }
                                
                                // MNG Kargo formatı
                                if (string.IsNullOrEmpty(trackingNumber))
                                {
                                    var mngMatches = Regex.Matches(requestContent, @"MNG\d{10}");
                                    if (mngMatches.Count > 0)
                                    {
                                        trackingNumber = mngMatches[mngMatches.Count - 1].Value;
                                    }
                                }

                                // Eğer kargo numarası bulunduysa ekle
                                if (!string.IsNullOrEmpty(trackingNumber))
                                {
                                    // Eğer bu takip numarası daha önce işlenmediyse ekle
                                    if (!islenenKargolar.Contains(trackingNumber))
                                    {
                                        islenenKargolar.Add(trackingNumber);
                                        bulunanKargoSayisi++;

                                        var kargoData = new KargoData
                                        {
                                            TrackingNumber = trackingNumber,
                                            StoreId = magazaId,
                                            RequestId = talepId,
                                            RequestSubject = konu,
                                            Status = "Beklemede",
                                            EstimatedDelivery = "-",
                                            LastUpdated = DateTime.Now
                                        };

                                        await AddKargo(kargoData);
                                        yeniEklenenKargolar.Add(kargoData);
                                        _logger.LogInformation($"Kargo eklendi: {trackingNumber} - Mağaza: {magazaId} - Talep: {talepId}");
                                    }
                                    else
                                    {
                                        _logger.LogInformation($"Kargo numarası {trackingNumber} daha önce işlenmiş, atlanıyor.");
                                    }
                                }
                                else
                                {
                                    _logger.LogWarning($"Talep ID {talepId} için kargo numarası bulunamadı.");
                                }

                                // Geri dön
                                driver.Navigate().Back();
                                await Task.Delay(500); // Geri dönmek için bekleme süresi düşürüldü
                                islenenKargolar.Add("TALEP_" + talepId); // İşlendi olarak işaretle
                                processedInIteration++;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Talep işlenirken hata oluştu: {talepId}");
                                islenenKargolar.Add("TALEP_" + talepId); // İşlendi olarak işaretle
                                processedInIteration++;
                                try { driver.Navigate().Back(); await Task.Delay(500); } catch { } // Hata durumunda da geri dönme bekleme süresi düşürüldü
                                continue;
                            }
                        }

                        // Döngü sonunda işlenen talep sayısını kontrol et
                        int currentProcessedCount = islenenKargolar.Count(k => k.StartsWith("TALEP_"));
                        if (currentProcessedCount >= totalItems)
                        {
                            _logger.LogInformation("Tüm talepler işlendi. Döngü sonlandırılıyor.");
                            break;
                        }

                        // Eğer bu iterasyonda hiç yeni talep işlenmediyse
                        if (processedInIteration == 0)
                        {
                             noProgressCount++;
                            _logger.LogInformation($"Bu iterasyonda yeni talep işlenmedi. İlerleme olmayan deneme sayısı: {noProgressCount}");
                            if (noProgressCount >= maxNoProgress)
                            {
                                _logger.LogWarning($"{maxNoProgress} denemedir yeni talep işlenemiyor. Tüm taleplerin yüklenmemiş olabileceği veya başka bir sorun olabileceği düşünülüyor. İşlem sonlandırılıyor.");
                                break; // Belirli sayıda denemeye rağmen ilerleme yoksa döngüyü sonlandır
                            }
                        }
                        else
                        {
                            noProgressCount = 0; // İlerleme olduysa sayacı sıfırla
                        }

                        // Aşağı kaydır ve yeni elementlerin yüklenmesini bekle
                        _logger.LogInformation("Aşağı kaydırılıyor...");
                        driver.ExecuteScript("arguments[0].scrollTop += 150;", scrollContainer); // 150 piksel aşağı kaydır
                        await Task.Delay(1500); // Yeni içeriğin yüklenmesi için bekle

                        // Scroll sonrası toplam talep sayısını kontrol et (sadece bilgi amaçlı)
                        var afterScrollTaleplerCount = driver.FindElements(By.CssSelector("div.grid-row")).Count;
                        _logger.LogInformation($"Scroll sonrası toplam {afterScrollTaleplerCount} talep elementine ulaşıldı.");
                    }

                    _logger.LogInformation($"İşlem tamamlandı. Toplam {islenenKargolar.Count(k => k.StartsWith("TALEP_"))} talep işlendi, {bulunanKargoSayisi} kargo bulundu.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "4me verileri yüklenirken hata oluştu");
                    throw;
                }
            }
        }

        public async Task DeleteKargo(string trackingNumber)
        {
            if (string.IsNullOrEmpty(trackingNumber))
                return;

            var kargo = _kargoList.FirstOrDefault(k => k.TrackingNumber == trackingNumber);
            if (kargo != null)
            {
                _kargoList.Remove(kargo);
                SaveKargoData();
                _logger.LogInformation($"Kargo silindi: {trackingNumber}");
            }
        }

        public async Task DeleteAllKargos()
        {
            _kargoList.Clear();
            SaveKargoData();
            _logger.LogInformation("Tüm kargolar silindi.");
            await Task.CompletedTask;
        }
    }

    public class KargoData
    {
        [JsonPropertyName("takipNo")]
        public string TrackingNumber { get; set; } = "";

        [JsonPropertyName("magazaId")]
        public string StoreId { get; set; } = "";

        [JsonPropertyName("talepId")]
        public string RequestId { get; set; } = "";

        [JsonPropertyName("talepAdi")]
        public string RequestSubject { get; set; } = "";

        [JsonPropertyName("durum")]
        public string Status { get; set; } = "Beklemede";

        [JsonPropertyName("ongorulenTeslimat")]
        public string EstimatedDelivery { get; set; } = "-";

        [JsonPropertyName("sonGuncelleme")]
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
}
