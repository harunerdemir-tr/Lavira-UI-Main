using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace LaviraSON
{
    public partial class Form1 : Form
    {
        [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr Handle, int x, int y, int w, int h, bool repaint);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] static extern int SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);
        [DllImport("user32.dll")] static extern bool UpdateWindow(IntPtr hWnd);

        // Windows mesaj sabitleri — Unity embed focus/donma çözümü
        private const int WM_ACTIVATE = 0x0006;
        private const int WM_NCACTIVATE = 0x0086;
        private const int WM_SETFOCUS = 0x0007;
        private const int WM_PAINT = 0x000F;
        private const int WA_ACTIVE = 1;

        Process unityProcess;
        System.Windows.Forms.Timer unityKeepAliveTimer;
        System.Windows.Forms.Timer logFlushTimer;
        GMapOverlay katman;
        GMarkerGoogle roketIgnesi;
        GMarkerGoogle gorevIgnesi;
        GMapRoute roketYolu;
        GMapRoute gorevYolu;
        GMarkerGoogle rampaIgnesi;

        Control grafikYuvasi = null;
        string dosyaYolu = "";

        StreamWriter logWriter;
        private readonly object logKilidi = new object();

        UdpClient udpClient;
        IPEndPoint unityAdresi;
        SerialPort serialPortGorev = new SerialPort();
        SerialPort serialPortHakem = new SerialPort();

        double RampaEnlem = 0, RampaBoylam = 0;
        bool RampaKaydedildi = false;

        private readonly string appDir = AppDomain.CurrentDomain.BaseDirectory;
        private readonly TelemetriDurumu telemetriDurumu = new TelemetriDurumu();
        private DurumSesiYoneticisi durumSesiYoneticisi;
        private readonly List<byte> anaByteList = new List<byte>();
        private readonly object anaBufferKilidi = new object(); // OPT-1: Buffer thread-safety
        private BlockingCollection<byte[]> anaKuyruk;
        private CancellationTokenSource anaIptal;
        private Task anaTask; // OPT-6: Task referansı saklandı

        private readonly List<byte> gorevByteList = new List<byte>();
        private readonly object gorevBufferKilidi = new object(); // OPT-1: Buffer thread-safety
        private BlockingCollection<byte[]> gorevKuyruk;
        private CancellationTokenSource gorevIptal;
        private Task gorevTask; // OPT-6: Task referansı saklandı
        private SynchronizationContext uiContext;

        // Paket sayacı (thread-safe artış için Interlocked)
        private int paketSayaci = 0;
        private int gorevPaketSayaci = 0;

        // OPT-4: Harita 10 Hz limiti için son güncelleme zamanı
        private int sonHaritaGuncelleme = 0;
        private const int HaritaGuncellemeAraligi = 100; // ms

        // Ana gövde CSV: pitch/roll/yaw = 7,8,9 — Unity UDP için en az 10 alan yeterli
        private const int ANA_ALAN_MIN_UDP = 10;
        private const int ANA_ALAN_SAYISI = 27;

        private double hedefIrtifa = double.NaN, gosterilenIrtifa = double.NaN;
        private double hedefHiz = double.NaN, gosterilenHiz = double.NaN;
        private double hedefBasinc = double.NaN, gosterilenBasinc = double.NaN;
        private double hedefSicaklik = double.NaN, gosterilenSicaklik = double.NaN;
        private double hedefIvmeX = double.NaN, gosterilenIvmeX = double.NaN;
        private double hedefIvmeY = double.NaN, gosterilenIvmeY = double.NaN;
        private double hedefIvmeZ = double.NaN, gosterilenIvmeZ = double.NaN;

        private double hedefMinIrtifa = double.NaN, hedefMaxIrtifa = double.NaN;
        private double hedefMinHiz = double.NaN, hedefMaxHiz = double.NaN;
        private double hedefMinBasinc = double.NaN, hedefMaxBasinc = double.NaN;
        private double hedefMinSicaklik = double.NaN, hedefMaxSicaklik = double.NaN;
        private double hedefMinIvme = double.NaN, hedefMaxIvme = double.NaN;

        private System.Windows.Forms.Timer grafikAnimasyonTimer;
        private System.Diagnostics.Stopwatch animasyonStopwatch;

        public Form1()
        {
            InitializeComponent();
            pnlUnity.Resize += pnlUnity_Resize;
            // OPT-5: uiContext OnLoad'a taşındı, constructor'da atama yapılmıyor.
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            uiContext = SynchronizationContext.Current;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            pnlUnity.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.Resize += (s, ev) => { pnlUnity_Resize(null, null); };

            // Sol Panel Scroll desteği
            panel1.MouseEnter += (s, ev) => panel1.Focus();
            tableLayoutPanel3.MouseEnter += (s, ev) => panel1.Focus();

            // UDP
            udpClient = new UdpClient();
            unityAdresi = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5555);

            try
            {
                durumSesiYoneticisi = new DurumSesiYoneticisi();
            }
            catch (Exception ex) { Debug.WriteLine("Ses Yöneticisi Başlatılamadı: " + ex.Message); }

            // Log
            string zamanDamgasi = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string logKlasoru = Path.Combine(appDir, "LOG DOSYALARI");
            try
            {
                if (!Directory.Exists(logKlasoru))
                {
                    Directory.CreateDirectory(logKlasoru);
                }

                dosyaYolu = Path.Combine(logKlasoru, $"roket_log_{zamanDamgasi}.csv");
                logWriter = new StreamWriter(dosyaYolu, true); // AutoFlush kapalı
                logWriter.WriteLine(
                    "ZAMAN,KAYNAK,ANA_0_DURUM,ANA_1_IRT,ANA_2_HIZ,ANA_3_SIC,ANA_4,ANA_5_EN,ANA_6_BOY," +
                    "ANA_7_PITCH,ANA_8_ROLL,ANA_9_YAW,ANA_10_BASMS,ANA_11_BASBMP,ANA_12_BASTOP,ANA_13,ANA_14,ANA_15," +
                    "ANA_16,ANA_17,ANA_18,ANA_19,ANA_20_IVMEX,ANA_21_IVMEY,ANA_22_IVMEZ,ANA_23,ANA_24,ANA_25,ANA_26," +
                    "GOREV_0_FILTREX,GOREV_1_FILTREY,GOREV_2_FILTREZ,GOREV_3_HAM_IVMEX,GOREV_4_HAM_IVMEY,GOREV_5_HAM_IVMEZ,GOREV_6_ENLEM,GOREV_7_BOYLAM,GOREV_8,GOREV_9"
                );

                // Periyodik olarak (1 saniyede bir) logları diske yaz (Flush)
                logFlushTimer = new System.Windows.Forms.Timer();
                logFlushTimer.Interval = 1000;
                logFlushTimer.Tick += (s, ev) => 
                {
                    lock (logKilidi)
                    {
                            logWriter?.Flush();
                        
                    }
                };
                logFlushTimer.Start();
            }
            catch (Exception ex) { MessageBox.Show("Log dosyası oluşturulamadı: " + ex.Message); }

            // Portları doldur
            try
            {
                string[] portlar = SerialPort.GetPortNames();
                cmbPorts.Items.AddRange(portlar);
                if (cmbGorevPort != null) cmbGorevPort.Items.AddRange(portlar);
                if (cmbHakemPort != null) cmbHakemPort.Items.AddRange(portlar);

                cmbBaud.Items.AddRange(new object[] { "9600", "19200", "57600", "115200" });
                if (cmbPorts.Items.Count > 0) cmbPorts.SelectedIndex = 0;
                cmbBaud.SelectedItem = "115200";
            }
            catch (Exception ex) { Debug.WriteLine("Port Doldurma Hatası: " + ex.Message); }

            // Grafikler
            TemizleGrafikler();
            foreach (var chart in new[] { chartIrtifa, chartHiz, chartBasinc, chartSicaklik, chartIvme })
            {
                if (chart != null)
                {
                    chart.AntiAliasing = System.Windows.Forms.DataVisualization.Charting.AntiAliasingStyles.All;
                    chart.TextAntiAliasingQuality = System.Windows.Forms.DataVisualization.Charting.TextAntiAliasingQuality.High;
                    if (chart.ChartAreas.Count > 0)
                    {
                        chart.ChartAreas[0].AxisY.Maximum = double.NaN;
                        chart.ChartAreas[0].AxisY.Minimum = double.NaN;
                        chart.ChartAreas[0].AxisY.IsStartedFromZero = false;
                    }
                    chart.DoubleClick -= Grafik_DoubleClick;
                    chart.DoubleClick += Grafik_DoubleClick;
                }
            }

            grafikAnimasyonTimer = new System.Windows.Forms.Timer();
            grafikAnimasyonTimer.Interval = 33; // ~30 FPS
            animasyonStopwatch = new System.Diagnostics.Stopwatch();
            animasyonStopwatch.Start();
            grafikAnimasyonTimer.Tick += GrafikAnimasyonTimer_Tick;
            grafikAnimasyonTimer.Start();

            // Harita
            try
            {
                gMapControl1.MapProvider = GMapProviders.GoogleSatelliteMap;
                GMaps.Instance.Mode = AccessMode.ServerAndCache;
                gMapControl1.CacheLocation = Path.Combine(appDir, "HaritaDepo");
                gMapControl1.Position = new PointLatLng(40.743336617541964, 29.941275119807784);
                gMapControl1.MinZoom = 5; gMapControl1.MaxZoom = 20; gMapControl1.Zoom = 13;
                gMapControl1.ShowCenter = false; gMapControl1.DragButton = MouseButtons.Left;

                katman = new GMapOverlay("roket_katmani");
                gMapControl1.Overlays.Add(katman);

                roketYolu = new GMapRoute("roket_yolu") { Stroke = new Pen(Color.Red, 3) };
                katman.Routes.Add(roketYolu);

                gorevYolu = new GMapRoute("gorev_yolu") { Stroke = new Pen(Color.Green, 3) };
                katman.Routes.Add(gorevYolu);

                Bitmap roketIcon = new Bitmap(Properties.Resources.roket, new Size(52, 52));
                roketIgnesi = new GMarkerGoogle(gMapControl1.Position, roketIcon) { ToolTipText = "Roket" };
                roketIgnesi.Offset = new Point(-26, -26);

                Bitmap gorevIcon = new Bitmap(Properties.Resources.gorev, new Size(35, 35));
                gorevIgnesi = new GMarkerGoogle(gMapControl1.Position, gorevIcon) { ToolTipText = "Görev Yükü" };
                gorevIgnesi.Offset = new Point(-17, -17);
                gorevIgnesi.IsVisible = true;

                katman.Markers.Add(gorevIgnesi);
                katman.Markers.Add(roketIgnesi);
            }
            catch (Exception ex) { Debug.WriteLine("Harita Ayar Hatası: " + ex.Message); }

            UnityGomVeBaslat();

            lblMesafe.Parent = gMapControl1;
            lblMesafe.BackColor = Color.NavajoWhite;
            lblMesafe.ForeColor = Color.Black;
            lblMesafe.Font = new Font("Arial", 13, FontStyle.Bold);
            lblMesafe.AutoSize = true;
            lblMesafe.Text = "";
            lblMesafe.Visible = false;

            if (lblGorevMesafe != null)
            {
                lblGorevMesafe.Parent = gMapControl1;
                lblGorevMesafe.BackColor = Color.Orange;
                lblGorevMesafe.ForeColor = Color.MidnightBlue;
                lblGorevMesafe.Font = new Font("Arial", 13, FontStyle.Bold);
                lblGorevMesafe.AutoSize = true;
                lblGorevMesafe.Text = "GÖREV MESAFESİ: 0 m";
                lblGorevMesafe.Visible = true;
            }

            gMapControl1.MouseDoubleClick += gMapControl1_MouseDoubleClick;
            _ = OtomatikRampaKonumuAl();
        }

        private void btnBaglan_Click(object sender, EventArgs e)
        {
            if (!serialPort1.IsOpen)
            {
                // — Bağlan —
                if (string.IsNullOrWhiteSpace(cmbPorts.Text))
                { MessageBox.Show("Port seçmedin!"); return; }

                try
                {
                    serialPort1.PortName = cmbPorts.Text;
                    serialPort1.BaudRate = Convert.ToInt32(cmbBaud.Text);
                    serialPort1.DtrEnable = false; // ESP32/STM32 reset döngüsünü engeller
                    serialPort1.RtsEnable = false;

                    // Kuyruk ve token oluştur
                    anaIptal = new CancellationTokenSource();
                    lock (anaBufferKilidi) { anaByteList.Clear(); }
                    anaKuyruk = new BlockingCollection<byte[]>(boundedCapacity: 500);

                    // DataReceived event'i bağla
                    serialPort1.DataReceived += serialPort1_DataReceived;
                    serialPort1.Open();

                    // OPT-6: Task referansı class seviyesinde saklanıyor
                    anaTask = Task.Run(() => AnaKuyrukIsleyici(anaIptal.Token));

                    btnBaglan.Text = "KOPAR";
                    btnBaglan.BackColor = Color.Red;
                    btnBaglan.ForeColor = Color.White;
                }
                catch (Exception ex) { MessageBox.Show("Ana Port Hatası: " + ex.Message); }
            }
            else
            {
                // — Kopar —
                AnaGovdeBaglantiKapat();
                btnBaglan.Text = "BAĞLAN";
                btnBaglan.BackColor = Color.Green;
                btnBaglan.ForeColor = Color.Black;
            }
        }

        private void AnaGovdeBaglantiKapat()
        {
            try
            {
                // Önce token'ı iptal et — consumer task durur
                anaIptal?.Cancel();

                // DataReceived event'ini ayır
                serialPort1.DataReceived -= serialPort1_DataReceived;

                if (serialPort1.IsOpen) serialPort1.Close();

                // Kuyruğu kapat — GetConsumingEnumerable sonlanır
                anaKuyruk?.CompleteAdding();

                // Buffer'ı temizle (lock altında)
                lock (anaBufferKilidi) { anaByteList.Clear(); }
            }
            catch (Exception ex) { Debug.WriteLine("Ana Bağlantı Kapama Hatası: " + ex.Message); }
        }

        private void btnGorevBaglan_Click(object sender, EventArgs e)
        {
            if (!serialPortGorev.IsOpen)
            {
                // — Bağlan —
                if (cmbGorevPort == null || string.IsNullOrWhiteSpace(cmbGorevPort.Text))
                { MessageBox.Show("Görev portu seçmedin!"); return; }

                try
                {
                    serialPortGorev.PortName = cmbGorevPort.Text;
                    serialPortGorev.BaudRate = Convert.ToInt32(cmbBaud.Text);
                    serialPortGorev.DtrEnable = false;
                    serialPortGorev.RtsEnable = false;

                    // Kuyruk ve token oluştur
                    gorevIptal = new CancellationTokenSource();
                    lock (gorevBufferKilidi) { gorevByteList.Clear(); }
                    gorevKuyruk = new BlockingCollection<byte[]>(boundedCapacity: 500);

                    // DataReceived event'i bağla
                    serialPortGorev.DataReceived += serialPortGorev_DataReceived;
                    serialPortGorev.Open();

                    // OPT-6: Task referansı class seviyesinde saklanıyor
                    gorevTask = Task.Run(() => GorevKuyrukIsleyici(gorevIptal.Token));

                    btnGorevBaglan.Text = "KOPAR";
                    btnGorevBaglan.BackColor = Color.Red;
                    btnGorevBaglan.ForeColor = Color.White;
                }
                catch (Exception ex) { MessageBox.Show("Görev Port Hatası: " + ex.Message); }
            }
            else
            {
                // — Kopar —
                GorevYukuBaglantiKapat();
                btnGorevBaglan.Text = "BAĞLAN";
                btnGorevBaglan.BackColor = Color.Green;
                btnGorevBaglan.ForeColor = Color.Black;
            }
        }

        private void GorevYukuBaglantiKapat()
        {
            try
            {
                gorevIptal?.Cancel();
                serialPortGorev.DataReceived -= serialPortGorev_DataReceived;
                if (serialPortGorev.IsOpen) serialPortGorev.Close();
                gorevKuyruk?.CompleteAdding();
                // Buffer'ı temizle (lock altında)
                lock (gorevBufferKilidi) { gorevByteList.Clear(); }
            }
            catch (Exception ex) { Debug.WriteLine("Görev Bağlantı Kapama Hatası: " + ex.Message); }
        }

        private void btnHakemBaglan_Click(object sender, EventArgs e)
        {
            try
            {
                if (!serialPortHakem.IsOpen)
                {
                    if (cmbHakemPort == null || string.IsNullOrWhiteSpace(cmbHakemPort.Text))
                    { MessageBox.Show("Hakem portu seçilmedi!"); return; }

                    serialPortHakem.PortName = cmbHakemPort.Text;
                    serialPortHakem.BaudRate = 19200;
                    serialPortHakem.DataBits = 8;
                    serialPortHakem.Parity = Parity.None;
                    serialPortHakem.StopBits = StopBits.One;
                    serialPortHakem.DtrEnable = true;
                    serialPortHakem.RtsEnable = true;
                    serialPortHakem.Open();

                    btnHakemBaglan.Text = "KOPAR";
                    btnHakemBaglan.BackColor = Color.Red;
                    btnHakemBaglan.ForeColor = Color.White;
                }
                else
                {
                    serialPortHakem.Close();
                    btnHakemBaglan.Text = "BAĞLAN";
                    btnHakemBaglan.BackColor = Color.Green;
                    btnHakemBaglan.ForeColor = Color.Black;
                }
            }
            catch (Exception ex) { MessageBox.Show("Hakem Port Hatası: " + ex.Message); }
        }

        private void serialPort1_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (!serialPort1.IsOpen) return;

                int bytesToRead = serialPort1.BytesToRead;
                if (bytesToRead <= 0) return;

                byte[] buffer = new byte[bytesToRead];
                int bytesRead;
                try
                {
                    bytesRead = serialPort1.Read(buffer, 0, bytesToRead);
                }
                catch (IOException ioEx)
                {
                    Debug.WriteLine("Ana Port IO Hatası: " + ioEx.Message);
                    uiContext?.Post(_ =>
                    {
                        AnaGovdeBaglantiKapat();
                        btnBaglan.Text = "BAĞLAN";
                        btnBaglan.BackColor = Color.Green;
                        btnBaglan.ForeColor = Color.Black;
                        MessageBox.Show("Ana port bağlantısı kesildi! (IO Hatası)", "Bağlantı Koptu",
                                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }, null);
                    return;
                }
                catch (UnauthorizedAccessException uaEx)
                {
                    Debug.WriteLine("Ana Port Erişim Hatası: " + uaEx.Message);
                    uiContext?.Post(_ =>
                    {
                        AnaGovdeBaglantiKapat();
                        btnBaglan.Text = "BAĞLAN";
                        btnBaglan.BackColor = Color.Green;
                        btnBaglan.ForeColor = Color.Black;
                        MessageBox.Show("Ana porta erişim reddedildi!", "Bağlantı Koptu",
                                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }, null);
                    return;
                }

                if (bytesRead <= 0) return;

                lock (anaBufferKilidi)
                {
                    for (int i = 0; i < bytesRead; i++)
                        anaByteList.Add(buffer[i]);

                    if (anaByteList.Count > 8000)
                    {
                        Debug.WriteLine("ANA BYTE BUFFER TAŞTI, TEMİZLENDİ");
                        anaByteList.Clear();
                        return;
                    }

                    // Saf 61-Byte UKB Aviyonik Binary Paket Ayrıştırıcı: [0xAB][Durum][56-byte Float][1-byte Checksum][0x0D][0x0A]
                    while (anaByteList.Count >= 1)
                    {
                        if (anaByteList[0] == 0xAB)
                        {
                            if (anaByteList.Count < 61) break;

                            // 1. Kademe: Footer Kontrolü (\r\n)
                            if (anaByteList[59] != 0x0D || anaByteList[60] != 0x0A)
                            {
                                anaByteList.RemoveRange(0, 1);
                                continue;
                            }

                            // 2. Kademe: Checksum Kontrolü
                            uint sum = 0;
                            for (int i = 0; i < 58; i++) sum += anaByteList[i];

                            if ((byte)(sum & 0xFF) == anaByteList[58])
                            {
                                byte[] paket = anaByteList.GetRange(0, 61).ToArray();
                                anaByteList.RemoveRange(0, 61);
                                if (anaKuyruk != null && !anaKuyruk.IsAddingCompleted)
                                    anaKuyruk.Add(paket);
                            }
                            else
                            {
                                anaByteList.RemoveRange(0, 1);
                            }
                            continue;
                        }

                        int sonrakiHeader = -1;
                        for (int i = 1; i < anaByteList.Count; i++)
                        {
                            if (anaByteList[i] == 0xAB)
                            {
                                sonrakiHeader = i;
                                break;
                            }
                        }

                        if (sonrakiHeader > 0)
                        {
                            anaByteList.RemoveRange(0, sonrakiHeader);
                        }
                        else
                        {
                            anaByteList.Clear();
                            break;
                        }
                    }
                }
            }
            catch (InvalidOperationException) { /* Kuyruk kapandı */ }
            catch (Exception ex) { Debug.WriteLine("Ana DataReceived Hatası: " + ex.Message); }
        }

        private static string FloatToStr(float val)
        {
            return val.ToString(CultureInfo.InvariantCulture);
        }

        private static float ReadFloatBE(byte[] b, int offset)
        {
            if (offset + 4 > b.Length) return 0f;
            byte[] temp = new byte[4];
            temp[0] = b[offset + 3];
            temp[1] = b[offset + 2];
            temp[2] = b[offset + 1];
            temp[3] = b[offset];
            return BitConverter.ToSingle(temp, 0);
        }

        private static float ReadFloatLE(byte[] b, int offset)
        {
            if (offset + 4 > b.Length) return 0f;
            return BitConverter.ToSingle(b, offset);
        }

        private void serialPortGorev_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (!serialPortGorev.IsOpen) return;

                int bytesToRead = serialPortGorev.BytesToRead;
                if (bytesToRead <= 0) return;

                byte[] buffer = new byte[bytesToRead];
                int bytesRead;
                try
                {
                    bytesRead = serialPortGorev.Read(buffer, 0, bytesToRead);
                }
                catch (IOException ioEx)
                {
                    Debug.WriteLine("Görev Port IO Hatası: " + ioEx.Message);
                    uiContext?.Post(_ =>
                    {
                        GorevYukuBaglantiKapat();
                        btnGorevBaglan.Text = "BAĞLAN";
                        btnGorevBaglan.BackColor = Color.Green;
                        btnGorevBaglan.ForeColor = Color.Black;
                        MessageBox.Show("Görev yükü portu bağlantısı kesildi! (IO Hatası)", "Bağlantı Koptu",
                                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }, null);
                    return;
                }
                catch (UnauthorizedAccessException uaEx)
                {
                    Debug.WriteLine("Görev Port Erişim Hatası: " + uaEx.Message);
                    uiContext?.Post(_ =>
                    {
                        GorevYukuBaglantiKapat();
                        btnGorevBaglan.Text = "BAĞLAN";
                        btnGorevBaglan.BackColor = Color.Green;
                        btnGorevBaglan.ForeColor = Color.Black;
                        MessageBox.Show("Görev yükü portuna erişim reddedildi!", "Bağlantı Koptu",
                                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }, null);
                    return;
                }

                if (bytesRead <= 0) return;

                lock (gorevBufferKilidi)
                {
                    for (int i = 0; i < bytesRead; i++)
                        gorevByteList.Add(buffer[i]);

                    if (gorevByteList.Count > 8000)
                    {
                        Debug.WriteLine("GÖREV BYTE BUFFER TAŞTI, TEMİZLENDİ");
                        gorevByteList.Clear();
                        return;
                    }

                    // Saf 35-Byte STM32 Binary Paket Ayrıştırıcı: [0xAA][0x55][32-byte Float][1-byte XOR]
                    while (gorevByteList.Count >= 2)
                    {
                        if (gorevByteList[0] == 0xAA && gorevByteList[1] == 0x55)
                        {
                            if (gorevByteList.Count < 35) break;

                            byte xorHesap = 0;
                            for (int i = 2; i < 34; i++) xorHesap ^= gorevByteList[i];

                            if (xorHesap == gorevByteList[34])
                            {
                                byte[] paket = gorevByteList.GetRange(0, 35).ToArray();
                                gorevByteList.RemoveRange(0, 35);
                                if (gorevKuyruk != null && !gorevKuyruk.IsAddingCompleted)
                                    gorevKuyruk.Add(paket);
                            }
                            else
                            {
                                gorevByteList.RemoveRange(0, 1);
                            }
                            continue;
                        }

                        int sonrakiHeader = -1;
                        for (int i = 1; i < gorevByteList.Count - 1; i++)
                        {
                            if (gorevByteList[i] == 0xAA && gorevByteList[i + 1] == 0x55)
                            {
                                sonrakiHeader = i;
                                break;
                            }
                        }

                        if (sonrakiHeader > 0)
                        {
                            gorevByteList.RemoveRange(0, sonrakiHeader);
                        }
                        else
                        {
                            if (gorevByteList[gorevByteList.Count - 1] == 0xAA)
                            {
                                if (gorevByteList.Count > 1)
                                    gorevByteList.RemoveRange(0, gorevByteList.Count - 1);
                            }
                            else
                            {
                                gorevByteList.Clear();
                            }
                            break;
                        }
                    }
                }
            }
            catch (InvalidOperationException) { /* Kuyruk kapandı */ }
            catch (Exception ex) { Debug.WriteLine("Görev DataReceived Hatası: " + ex.Message); }
        }

        private void AnaKuyrukIsleyici(CancellationToken token)
        {
            try
            {
                foreach (byte[] b in anaKuyruk.GetConsumingEnumerable(token))
                {
                    if (b == null || b.Length != 61 || b[0] != 0xAB) continue;

                    // Ek Güvenlik: Checksum & Footer doğrulaması
                    if (b[59] != 0x0D || b[60] != 0x0A) continue;
                    uint sum = 0;
                    for (int i = 0; i < 58; i++) sum += b[i];
                    if ((byte)(sum & 0xFF) != b[58]) continue;

                    string[] p = new string[ANA_ALAN_SAYISI];
                    for (int i = 0; i < p.Length; i++) p[i] = "0";

                    // =========================================================================
                    // AVİYONİK UKB LORA 61-BYTE PAKET PARSE (Big-Endian Float32)
                    // =========================================================================
                    byte durumKodu = b[1];
                    float irtifa = ReadFloatBE(b, 2);
                    float hiz = ReadFloatBE(b, 6);
                    float sicaklik = ReadFloatBE(b, 10);
                    float gpsEnlem = ReadFloatBE(b, 14);
                    float gpsBoylam = ReadFloatBE(b, 18);
                    float pitch = ReadFloatBE(b, 22);
                    float roll = ReadFloatBE(b, 26);
                    float yaw = ReadFloatBE(b, 30);
                    float basincMS = ReadFloatBE(b, 34);
                    float basincBMP = ReadFloatBE(b, 38);
                    float basincToplam = ReadFloatBE(b, 42);
                    float ivmeX = ReadFloatBE(b, 46);
                    float ivmeY = ReadFloatBE(b, 50);
                    float ivmeZ = ReadFloatBE(b, 54);

                    p[0] = durumKodu.ToString();
                    p[1] = FloatToStr(irtifa);
                    p[2] = FloatToStr(hiz);
                    p[3] = FloatToStr(sicaklik);
                    p[4] = "0";
                    p[5] = FloatToStr(gpsEnlem);
                    p[6] = FloatToStr(gpsBoylam);
                    p[7] = FloatToStr(pitch);
                    p[8] = FloatToStr(roll);
                    p[9] = FloatToStr(yaw);
                    p[10] = FloatToStr(basincMS);
                    p[11] = FloatToStr(basincBMP);
                    p[12] = FloatToStr(basincToplam);
                    p[13] = FloatToStr(irtifa);
                    p[20] = FloatToStr(ivmeX);
                    p[21] = FloatToStr(ivmeY);
                    p[22] = FloatToStr(ivmeZ);
                    p[23] = FloatToStr(ivmeX);
                    p[24] = FloatToStr(ivmeY);
                    p[25] = FloatToStr(ivmeZ);

                    Interlocked.Increment(ref paketSayaci);

                    telemetriDurumu.AnaGuncelle(p);
                    LogYaz("ANA");
                    UdpGonder(p);
                    HakemGonder();
                    uiContext?.Post(_ => AnaGovdeUIGuncelle(p), null);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.WriteLine("AnaKuyrukIsleyici Hata: " + ex.Message); }
        }

        private void GorevKuyrukIsleyici(CancellationToken token)
        {
            try
            {
                foreach (byte[] b in gorevKuyruk.GetConsumingEnumerable(token))
                {
                    if (b == null || b.Length == 0) continue;

                    string[] p = new string[10];
                    for (int i = 0; i < p.Length; i++) p[i] = "0";

                    if (b.Length != 35 || b[0] != 0xAA || b[1] != 0x55)
                    {
                        continue;
                    }

                    // Ek Güvenlik: XOR Checksum doğrulaması
                    byte xorHesap = 0;
                    for (int i = 2; i < 34; i++) xorHesap ^= b[i];
                    if (xorHesap != b[34])
                    {
                        Debug.WriteLine("[GÖREV KUYRUK XOR HATASI] Bozuk paket işlenmeden atıldı.");
                        continue;
                    }

                    // STM32 LoRa Binary Format: [0xAA][0x55][8xfloat32][XOR]
                    // Sıra: hx, fx, hy, fy, hz, fz, lat, lon
                    float hx = BitConverter.ToSingle(b, 2);
                    float fx = BitConverter.ToSingle(b, 6);
                    float hy = BitConverter.ToSingle(b, 10);
                    float fy = BitConverter.ToSingle(b, 14);
                    float hz = BitConverter.ToSingle(b, 18);
                    float fz = BitConverter.ToSingle(b, 22);
                    float gLat = BitConverter.ToSingle(b, 26);
                    float gLon = BitConverter.ToSingle(b, 30);

                    p[0] = FloatToStr(fx); // Filtreli X
                    p[1] = FloatToStr(fy); // Filtreli Y
                    p[2] = FloatToStr(fz); // Filtreli Z
                    p[3] = FloatToStr(hx); // Ham Ivme X
                    p[4] = FloatToStr(hy); // Ham Ivme Y
                    p[5] = FloatToStr(hz); // Ham Ivme Z
                    p[6] = FloatToStr(gLat); // Enlem
                    p[7] = FloatToStr(gLon); // Boylam

                    Interlocked.Increment(ref gorevPaketSayaci);

                    // 1. Ortak veri havuzunu güncelle
                    telemetriDurumu.GorevGuncelle(p);

                    // 2. Log yaz
                    LogYaz("GOREV");

                    // 3. Hakem paketi gönder
                    HakemGonder();

                    // 4. UI güncelle — sadece görev yükü alanları
                    uiContext?.Post(_ => GorevYukuUIGuncelle(p), null);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.WriteLine("GorevKuyrukIsleyici Hata: " + ex.Message); }
        }
        private void AnaGovdeUIGuncelle(string[] p)
        {
            try
            {
                string Get(int i) => (i < p.Length && !string.IsNullOrWhiteSpace(p[i]) && p[i] != "NULL")
                                      ? p[i].Trim() : "NULL";

                lblDurum.Text = Get(0) != "NULL" ? UcusDurumString(Get(0)) : "NULL";

                if (int.TryParse(Get(0), out int durumKodu))
                {
                    durumSesiYoneticisi?.DurumGuncelle(durumKodu);
                }

                lblAnaIrtifa.Text = Get(1);
                lblAnaHiz.Text = Get(2);
                if (lblAnaSicaklik != null) lblAnaSicaklik.Text = Get(3);
                if (lblGpsEnlem != null) lblGpsEnlem.Text = Get(5);
                if (lblGpsBoylam != null) lblGpsBoylam.Text = Get(6);
                if (lblPitch != null) lblPitch.Text = Get(7);
                if (lblRoll != null) lblRoll.Text = Get(8);
                if (lblYaw != null) lblYaw.Text = Get(9);
                if (lblBasincMS != null) lblBasincMS.Text = Get(10);
                if (lblBasincBMP != null) lblBasincBMP.Text = Get(11);
                if (lblBasincToplam != null) lblBasincToplam.Text = Get(12);
                if (lblFiltreX != null) lblFiltreX.Text = Get(23);
                if (lblFiltreY != null) lblFiltreY.Text = Get(24);
                if (lblFiltreZ != null) lblFiltreZ.Text = Get(25);
                lblPaket.Text = paketSayaci.ToString();

                // Grafikler
                if (Get(1) != "NULL") hedefIrtifa = DoubleParse(Get(1));
                if (Get(2) != "NULL") hedefHiz = DoubleParse(Get(2));
                if (Get(3) != "NULL") hedefSicaklik = DoubleParse(Get(3));
                if (Get(12) != "NULL") hedefBasinc = DoubleParse(Get(12));
                if (Get(23) != "NULL") hedefIvmeX = DoubleParse(Get(23));
                if (Get(24) != "NULL") hedefIvmeY = DoubleParse(Get(24));
                if (Get(25) != "NULL") hedefIvmeZ = DoubleParse(Get(25));
                if (GpsGecerliMi(Get(5), Get(6)))
                {
                    double lat = DoubleParse(Get(5));
                    double lng = DoubleParse(Get(6));

                    // OPT-4: Marker pozisyonunu her pakette güncelle (hafif işlem).
                    // Refresh() ise 10 Hz ile sınırlandırıldı.
                    roketIgnesi.Position = new PointLatLng(lat, lng);
                    
                    // Görev Yükü roketten ayrılana kadar roketle beraber hareket etsin ve görünür olsun.
                    if (int.TryParse(Get(0), out int geciciDurum) && geciciDurum < 4)
                    {
                        gorevIgnesi.Position = roketIgnesi.Position;
                        if (!gorevIgnesi.IsVisible) gorevIgnesi.IsVisible = true;

                        katman.Routes.Remove(gorevYolu);
                        gorevYolu.Points.Clear();
                        if (RampaKaydedildi)
                        {
                            gorevYolu.Points.Add(new PointLatLng(RampaEnlem, RampaBoylam));
                            gorevYolu.Points.Add(new PointLatLng(lat, lng));
                        }
                        katman.Routes.Add(gorevYolu);
                        gMapControl1.UpdateRouteLocalPosition(gorevYolu);
                    }

                    katman.Routes.Remove(roketYolu);
                    roketYolu.Points.Clear();
                    if (RampaKaydedildi)
                    {
                        roketYolu.Points.Add(new PointLatLng(RampaEnlem, RampaBoylam));
                        roketYolu.Points.Add(new PointLatLng(lat, lng));
                    }
                    katman.Routes.Add(roketYolu);
                    gMapControl1.UpdateRouteLocalPosition(roketYolu);

                    // OPT-4: 10 Hz harita güncelleme limiti
                    int simdi = Environment.TickCount;
                    if (simdi - sonHaritaGuncelleme >= HaritaGuncellemeAraligi)
                    {
                        sonHaritaGuncelleme = simdi;
                        gMapControl1.Refresh();
                    }

                    if (RampaKaydedildi)
                    {
                        double mesafe = HesaplaMesafe(RampaEnlem, RampaBoylam, lat, lng);
                        if (mesafe > 41000)
                        {
                            Debug.WriteLine($"[MESAFE KORUMASI] Absürt mesafe reddedildi: {mesafe:F0} m");
                            return; // Çıkış yap, işlem hatali
                        }

                        lblMesafe.Visible = mesafe > 5;
                        lblMesafe.Text = mesafe > 5 ? "Mesafe: " + mesafe.ToString("F0") + " m" : "";
                        if (mesafe > 5) 
                        { 
                            lblMesafe.Location = new Point(15, 15); 
                            lblMesafe.BringToFront(); 
                        }

                        if (lblGorevMesafe != null)
                        {
                            if (mesafe > 5)
                            {
                                lblGorevMesafe.Visible = true;
                                lblGorevMesafe.Text = "GÖREV MESAFESİ: " + mesafe.ToString("F0") + " m";
                                lblGorevMesafe.Location = new Point(15, 45);
                                lblGorevMesafe.BringToFront();
                            }
                            else
                            {
                                lblGorevMesafe.Visible = false;
                            }
                        }
                    }
                }

            }
            catch (Exception ex) { Debug.WriteLine("AnaGovdeUI Hata: " + ex.Message); }
        }
        private void GorevYukuUIGuncelle(string[] p)
        {
            try
            {
                string Get(int i) => (i < p.Length && !string.IsNullOrWhiteSpace(p[i]) && p[i] != "NULL")
                       ? p[i].Trim() : "NULL";
                if (lblGFiltreX != null) lblGFiltreX.Text = Get(0);
                if (lblGFiltreY != null) lblGFiltreY.Text = Get(1);
                if (lblGFiltreZ != null) lblGFiltreZ.Text = Get(2);
                lblIvmeX.Text = Get(3);
                lblIvmeY.Text = Get(4);
                lblIvmeZ.Text = Get(5);            
                if (lblGorevEnlem != null) lblGorevEnlem.Text = Get(6);
                if (lblGorevBoylam != null) lblGorevBoylam.Text = Get(7);
                if (lblGorevPaket != null) lblGorevPaket.Text = gorevPaketSayaci.ToString();
                if (lblGÖREVPAKETNO != null && lblGÖREVPAKETNO != lblGorevPaket) lblGÖREVPAKETNO.Text = gorevPaketSayaci.ToString();
                if (GpsGecerliMi(Get(6), Get(7)))
                {
                    double gLat = DoubleParse(Get(6));
                    double gLng = DoubleParse(Get(7));

                    // İşaretçi pozisyonunu doğrudan GPS verisiyle güncelle
                    gorevIgnesi.Position = new PointLatLng(gLat, gLng);
                    if (RampaKaydedildi)
                    {
                        double gMesafe = HesaplaMesafe(RampaEnlem, RampaBoylam, gorevIgnesi.Position.Lat, gorevIgnesi.Position.Lng);
                        bool gecerliMesafe = gMesafe <= 41000;
                        
                        gorevIgnesi.IsVisible = gecerliMesafe;
                        lblGorevMesafe.Visible = gecerliMesafe;

                        if (gecerliMesafe)
                        {
                            lblGorevMesafe.Text = "GÖREV MESAFESİ: " + gMesafe.ToString("F0") + " m";
                            lblGorevMesafe.Location = new Point(15, 45);
                            lblGorevMesafe.BringToFront();
                        }
                    }
                    else
                    {
                        gorevIgnesi.IsVisible = true;
                    }

                    gorevYolu.Points.Clear();
                    if (RampaKaydedildi)
                    {
                        gorevYolu.Points.Add(new PointLatLng(RampaEnlem, RampaBoylam));
                        gorevYolu.Points.Add(new PointLatLng(gLat, gLng));
                    }
                    gMapControl1.UpdateRouteLocalPosition(gorevYolu);

                    int simdi = Environment.TickCount;
                    if (simdi - sonHaritaGuncelleme >= HaritaGuncellemeAraligi)
                    {
                        sonHaritaGuncelleme = simdi;
                        gMapControl1.Refresh();
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine("GorevYukuUI Hata: " + ex.Message); }
        }
        private static bool GpsGecerliMi(string enlemStr, string boylamStr)
        {
            if (string.IsNullOrWhiteSpace(enlemStr) || string.IsNullOrWhiteSpace(boylamStr))
                return false;
            if (enlemStr == "NULL" || boylamStr == "NULL")
                return false;

            string enS = enlemStr.Trim().TrimStart('+').Replace(',', '.');
            string boS = boylamStr.Trim().TrimStart('+').Replace(',', '.');

            if (!double.TryParse(enS, NumberStyles.Any, CultureInfo.InvariantCulture, out double en))
                return false;
            if (!double.TryParse(boS, NumberStyles.Any, CultureInfo.InvariantCulture, out double bo))
                return false;

            // 0,0 koordinatı GPS kilitlenmediğinde gelir, harita için geçersiz say
            if (Math.Abs(en) < 0.0001 && Math.Abs(bo) < 0.0001)
                return false;

            // Geçerli dünya koordinat sınırları (-90..90 enlem, -180..180 boylam)
            if (en < -90.0 || en > 90.0 || bo < -180.0 || bo > 180.0)
                return false;

            return true;
        }

        private static string[] AlanlariNormallestir(string[] p, int beklenenAdet)
        {
            string[] sonuc = new string[beklenenAdet];
            for (int i = 0; i < beklenenAdet; i++)
                sonuc[i] = (i < p.Length && !string.IsNullOrWhiteSpace(p[i])) ? p[i].Trim() : "NULL";
            return sonuc;
        }

        private static string AciyiUnityIcin(string ham)
        {
            if (string.IsNullOrWhiteSpace(ham) || ham.Trim() == "NULL")
                return "0";
            string s = ham.Trim().Replace(',', '.');
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                return d.ToString(CultureInfo.InvariantCulture);
            return "0";
        }

        private static string CsvAlaniSarmalA(string deger)
        {
            if (deger == null) return "\"\"";
            // İçerdeki çift tırnakları ikiye katlayarak kaçış karakteri uygula (RFC 4180)
            return "\"" + deger.Replace("\"", "\"\"") + "\"";
        }

        private void LogYaz(string kaynak)
        {
            try
            {
                lock (logKilidi)
                {
                    if (logWriter != null && logWriter.BaseStream != null)
                    {
                        var (ana, gorev) = telemetriDurumu.Snapshot();

                        // Dizileri beklenen uzunluklara sabitliyoruz ki sütun kayması olmasın
                        string[] anaTam = new string[27];
                        if (ana != null) Array.Copy(ana, anaTam, Math.Min(ana.Length, 27));

                        string[] gorevTam = new string[10];
                        if (gorev != null) Array.Copy(gorev, gorevTam, Math.Min(gorev.Length, 10));

                        string zamanAlani = CsvAlaniSarmalA(DateTime.Now.ToString("HH:mm:ss.fff"));
                        string kaynakAlani = CsvAlaniSarmalA(kaynak);
                        
                        string anaAlanlari = string.Join(",", anaTam.Select(CsvAlaniSarmalA));
                        string gorevAlanlari = string.Join(",", gorevTam.Select(CsvAlaniSarmalA));
                        
                        logWriter.WriteLine($"{zamanAlani},{kaynakAlani},{anaAlanlari},{gorevAlanlari}");
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine("Log Hatası: " + ex.Message); }
        }

        private void UdpGonder(string[] p)
        {
            // p[0]=DurumKodu, p[7]=Pitch, p[8]=Roll, p[9]=Yaw
            // Unity RoketAlici formatı: pitch,roll,yaw,durumKodu
            if (p.Length <= 9) return;
            try
            {
                string pitch = AciyiUnityIcin(p[7]);
                string roll = AciyiUnityIcin(p[8]);
                string yaw = AciyiUnityIcin(p[9]);
                string durum = (p.Length > 0 && !string.IsNullOrWhiteSpace(p[0])) ? p[0].Trim() : "0";
                string pck = $"{pitch},{roll},{yaw},{durum}";
                byte[] udpData = Encoding.ASCII.GetBytes(pck);
                udpClient.Send(udpData, udpData.Length, unityAdresi);
            }
            catch (Exception ex) { Debug.WriteLine("Unity UDP Hatası: " + ex.Message); }
        }

        private void HakemGonder()
        {
            if (!serialPortHakem.IsOpen) return;
            try
            {
                var (ana, gorev) = telemetriDurumu.Snapshot();
                byte[] paket = HakemPaketleyici.PaketOlustur(ana, gorev);
                serialPortHakem.Write(paket, 0, paket.Length);
            }
            catch (Exception ex) { Debug.WriteLine("Hakem Gönderme Hatası: " + ex.Message); }
        }

        // OPT-10: DoubleParse metodu aynen korunuyor (text.Replace(',', '.') dahil).
        private double DoubleParse(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text == "NULL") return double.NaN;
            double.TryParse(text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double result);
            return result;
        }

        private string UcusDurumString(string stateCode)
        {
            switch (stateCode?.Trim())
            {
                case "0": return "0 - Beklemede / Bağlantı Kuruldu";
                case "1": return "1 - Roket Rampada";
                case "2": return "2 - Yükseliyor";
                case "3": return "3 - Birinci Ayrılma (Tepe Noktası)";
                case "4": return "4 - İkinci Ayrılma (Ana Paraşüt)";
                case "5": return "5 - Görev Yükü Ayrıldı";
                case "6": return "6 - İniş Yapıldı / Kurtarıldı";
                default: return stateCode ?? "0";
            }
        }

        private void GrafikAnimasyonTimer_Tick(object sender, EventArgs e)
        {
            double dt = animasyonStopwatch.Elapsed.TotalSeconds;
            animasyonStopwatch.Restart();

            if (dt <= 0 || double.IsNaN(dt)) dt = 0.033;
            if (dt > 0.1) dt = 0.1; // Limit dt to prevent huge jumps (max 100ms)

            double tIrtifa = 1.0 - Math.Exp(-5.0 * dt);
            double tHiz = 1.0 - Math.Exp(-6.6 * dt);
            double tBasinc = 1.0 - Math.Exp(-5.0 * dt);
            double tSicaklik = 1.0 - Math.Exp(-3.3 * dt);
            double tIvme = 1.0 - Math.Exp(-10.0 * dt);
            double tEksen = 1.0 - Math.Exp(-3.3 * dt);

            Yumusat(ref gosterilenIrtifa, hedefIrtifa, tIrtifa);
            Yumusat(ref gosterilenHiz, hedefHiz, tHiz);
            Yumusat(ref gosterilenBasinc, hedefBasinc, tBasinc);
            Yumusat(ref gosterilenSicaklik, hedefSicaklik, tSicaklik);
            Yumusat(ref gosterilenIvmeX, hedefIvmeX, tIvme);
            Yumusat(ref gosterilenIvmeY, hedefIvmeY, tIvme);
            Yumusat(ref gosterilenIvmeZ, hedefIvmeZ, tIvme);

            GrafigeNoktaEkle(chartIrtifa, gosterilenIrtifa, ref hedefMinIrtifa, ref hedefMaxIrtifa, tEksen);
            GrafigeNoktaEkle(chartHiz, gosterilenHiz, ref hedefMinHiz, ref hedefMaxHiz, tEksen);
            GrafigeNoktaEkle(chartBasinc, gosterilenBasinc, ref hedefMinBasinc, ref hedefMaxBasinc, tEksen);
            GrafigeNoktaEkle(chartSicaklik, gosterilenSicaklik, ref hedefMinSicaklik, ref hedefMaxSicaklik, tEksen);
            GrafigeCokluNoktaEkle(chartIvme, gosterilenIvmeX, gosterilenIvmeY, gosterilenIvmeZ, ref hedefMinIvme, ref hedefMaxIvme, tEksen);
        }

        private void Yumusat(ref double current, double target, double t)
        {
            if (double.IsNaN(target)) return;
            if (double.IsNaN(current)) current = target;
            current += (target - current) * t;
        }

        private void EksenYumusat(System.Windows.Forms.DataVisualization.Charting.Axis axis, double minTarget, double maxTarget, double t)
        {
            if (double.IsNaN(minTarget) || double.IsNaN(maxTarget)) return;

            if (double.IsNaN(axis.Minimum)) axis.Minimum = minTarget;
            else
            {
                double currentMin = axis.Minimum;
                if (minTarget < currentMin) axis.Minimum += (minTarget - currentMin) * 0.5;
                else axis.Minimum += (minTarget - currentMin) * t;
            }

            if (double.IsNaN(axis.Maximum)) axis.Maximum = maxTarget;
            else
            {
                double currentMax = axis.Maximum;
                if (maxTarget > currentMax) axis.Maximum += (maxTarget - currentMax) * 0.5;
                else axis.Maximum += (maxTarget - currentMax) * t;
            }
        }

        private void GrafigeNoktaEkle(System.Windows.Forms.DataVisualization.Charting.Chart grafik, double v1, ref double hedefMin, ref double hedefMax, double tEksen)
        {
            if (grafik == null || double.IsNaN(v1) || grafik.Series.Count == 0) return;
            grafik.Series[0].Points.AddY(v1);
            if (grafik.Series[0].Points.Count > 300) grafik.Series[0].Points.RemoveAt(0);

            HesaplaHedefEksen(grafik, ref hedefMin, ref hedefMax);
            EksenYumusat(grafik.ChartAreas[0].AxisY, hedefMin, hedefMax, tEksen);

            int toplamNokta = grafik.Series[0].Points.Count;
            grafik.ChartAreas[0].AxisX.Minimum = Math.Max(0, toplamNokta - 300);
            grafik.ChartAreas[0].AxisX.Maximum = Math.Max(300, toplamNokta);
        }

        private void GrafigeCokluNoktaEkle(System.Windows.Forms.DataVisualization.Charting.Chart grafik, double v1, double v2, double v3, ref double hedefMin, ref double hedefMax, double tEksen)
        {
            if (grafik == null || grafik.Series.Count == 0) return;
            
            if (!double.IsNaN(v1) && grafik.Series.Count >= 1) grafik.Series[0].Points.AddY(v1);
            if (!double.IsNaN(v2) && grafik.Series.Count >= 2) grafik.Series[1].Points.AddY(v2);
            if (!double.IsNaN(v3) && grafik.Series.Count >= 3) grafik.Series[2].Points.AddY(v3);

            foreach (var seri in grafik.Series)
                if (seri.Points.Count > 300) seri.Points.RemoveAt(0);

            HesaplaHedefEksen(grafik, ref hedefMin, ref hedefMax);
            EksenYumusat(grafik.ChartAreas[0].AxisY, hedefMin, hedefMax, tEksen);

            int toplamNokta = grafik.Series[0].Points.Count;
            grafik.ChartAreas[0].AxisX.Minimum = Math.Max(0, toplamNokta - 300);
            grafik.ChartAreas[0].AxisX.Maximum = Math.Max(300, toplamNokta);
        }

        private void HesaplaHedefEksen(System.Windows.Forms.DataVisualization.Charting.Chart grafik, ref double min, ref double max)
        {
            double localMin = double.MaxValue;
            double localMax = double.MinValue;

            foreach (var seri in grafik.Series)
            {
                foreach (var nokta in seri.Points)
                {
                    if (nokta.YValues[0] < localMin) localMin = nokta.YValues[0];
                    if (nokta.YValues[0] > localMax) localMax = nokta.YValues[0];
                }
            }

            if (localMin == double.MaxValue) return;
            if (Math.Abs(localMax - localMin) < 0.001)
            {
                min = localMin - 1.0;
                max = localMax + 1.0;
            }
            else
            {
                double margin = (localMax - localMin) * 0.15;
                min = localMin - margin;
                max = localMax + margin;
            }
        }

        private double HesaplaMesafe(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000;
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        private void TemizleGrafikler()
        {
            foreach (var chart in new[] { chartIrtifa, chartHiz, chartBasinc, chartSicaklik, chartIvme })
                if (chart != null)
                    foreach (var seri in chart.Series)
                        seri.Points.Clear();
        }
        private async Task OtomatikRampaKonumuAl()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    string response = await client.GetStringAsync("http://ip-api.com/json");
                    JObject json = JObject.Parse(response);

                    if (json["status"]?.ToString() == "success")
                    {
                        RampaEnlem = (double)json["lat"];
                        RampaBoylam = (double)json["lon"];
                        RampaKaydedildi = true;
                        Debug.WriteLine($"[RAMPA OTOMATIK] Konum IP'den çekildi: {RampaEnlem}, {RampaBoylam}");
                    }
                    else
                    {
                        throw new Exception("IP-API başarısız.");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RAMPA OTOMATIK HATA] IP'den konum çekilemedi: {ex.Message}");
                // Fallback (Varsayılan Kocaeli)
                RampaEnlem = 40.743336617541964;
                RampaBoylam = 29.941275119807784;
                RampaKaydedildi = true;
                Debug.WriteLine("[RAMPA FALLBACK] Varsayılan konum kullanıldı.");
            }

            RampaHaritadaGuncelle();
        }

        private void RampaHaritadaGuncelle()
        {
            if (rampaIgnesi != null) katman.Markers.Remove(rampaIgnesi);
            try
            {
                PointLatLng rampaKonum = new PointLatLng(RampaEnlem, RampaBoylam);
                Bitmap evIcon = new Bitmap(Properties.Resources.ev, new Size(52, 52));
                rampaIgnesi = new GMarkerGoogle(rampaKonum, evIcon);
                rampaIgnesi.Offset = new Point(-26, -26);
                rampaIgnesi.ToolTipText = "Yer İstasyonu";
                katman.Markers.Add(rampaIgnesi);
                gMapControl1.Position = rampaKonum;

                // Roket ve Görev Yükü başlangıçta Yer İstasyonu / Rampa ile aynı konumda başlasın
                if (roketIgnesi != null && paketSayaci == 0)
                {
                    roketIgnesi.Position = rampaKonum;
                }
                if (gorevIgnesi != null && gorevPaketSayaci == 0)
                {
                    gorevIgnesi.Position = rampaKonum;
                }
                
                // Veri gelmesini beklemeden anında mesafe ölçümü
                if (roketIgnesi != null)
                {
                    double mesafe = HesaplaMesafe(RampaEnlem, RampaBoylam, roketIgnesi.Position.Lat, roketIgnesi.Position.Lng);
                    if (mesafe <= 41000)
                    {
                        lblMesafe.Visible = mesafe > 5;
                        lblMesafe.Text = mesafe > 5 ? "Mesafe: " + mesafe.ToString("F0") + " m" : "";
                        if (mesafe > 5) 
                        { 
                            lblMesafe.Location = new Point(15, 15); 
                            lblMesafe.BringToFront(); 
                        }

                        katman.Routes.Remove(roketYolu);
                        roketYolu.Points.Clear();
                        roketYolu.Points.Add(new PointLatLng(RampaEnlem, RampaBoylam));
                        roketYolu.Points.Add(new PointLatLng(roketIgnesi.Position.Lat, roketIgnesi.Position.Lng));
                        katman.Routes.Add(roketYolu);
                        gMapControl1.UpdateRouteLocalPosition(roketYolu);
                    }
                    else
                    {
                        lblMesafe.Visible = false;
                    }
                }

                if (gorevIgnesi != null)
                {
                    double gMesafe = HesaplaMesafe(RampaEnlem, RampaBoylam, gorevIgnesi.Position.Lat, gorevIgnesi.Position.Lng);
                    if (gMesafe <= 41000)
                    {
                        gorevIgnesi.IsVisible = true;
                        lblGorevMesafe.Visible = true;
                        lblGorevMesafe.Text = "GÖREV MESAFESİ: " + gMesafe.ToString("F0") + " m";
                        lblGorevMesafe.Location = new Point(15, 45);
                        lblGorevMesafe.BringToFront();

                        katman.Routes.Remove(gorevYolu);
                        gorevYolu.Points.Clear();
                        gorevYolu.Points.Add(new PointLatLng(RampaEnlem, RampaBoylam));
                        gorevYolu.Points.Add(new PointLatLng(gorevIgnesi.Position.Lat, gorevIgnesi.Position.Lng));
                        katman.Routes.Add(gorevYolu);
                        gMapControl1.UpdateRouteLocalPosition(gorevYolu);
                    }
                    else
                    {
                        lblGorevMesafe.Visible = false;
                        gorevIgnesi.IsVisible = false;
                    }
                }

                gMapControl1.Refresh();
            }
            catch (Exception ex) { Debug.WriteLine("Rampa Güncelleme Hatası: " + ex.Message); }
        }

        private async void btnKonumSifirla_Click(object sender, EventArgs e)
        {
            btnKonumSifirla.Enabled = false;
            await OtomatikRampaKonumuAl();
            btnKonumSifirla.Enabled = true;
        }

        private void gMapControl1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                double lat = gMapControl1.FromLocalToLatLng(e.X, e.Y).Lat;
                double lng = gMapControl1.FromLocalToLatLng(e.X, e.Y).Lng;
                
                RampaEnlem = lat;
                RampaBoylam = lng;
                RampaKaydedildi = true;
                RampaHaritadaGuncelle();
                Debug.WriteLine($"[RAMPA MANUEL] Kullanıcı haritadan seçti: {lat}, {lng}");
            }
        }

        private void btnPortYenile_Click(object sender, EventArgs e)
        {
            try
            {
                cmbPorts.Items.Clear();
                if (cmbGorevPort != null) cmbGorevPort.Items.Clear();
                if (cmbHakemPort != null) cmbHakemPort.Items.Clear();

                string[] portlar = SerialPort.GetPortNames();
                cmbPorts.Items.AddRange(portlar);
                if (cmbGorevPort != null) cmbGorevPort.Items.AddRange(portlar);
                if (cmbHakemPort != null) cmbHakemPort.Items.AddRange(portlar);

                if (cmbPorts.Items.Count > 0) cmbPorts.SelectedIndex = 0;

            }
            catch (Exception ex) { Debug.WriteLine("Port Yenileme Hatası: " + ex.Message); }
        }

        private void Grafik_DoubleClick(object sender, EventArgs e)
        {
            var grafik = (System.Windows.Forms.DataVisualization.Charting.Chart)sender;
            if (grafik.Parent != this)
            {
                grafikYuvasi = grafik.Parent;
                grafik.Parent = this;
                grafik.BringToFront();
                grafik.Dock = DockStyle.Fill;
            }
            else
            {
                grafik.Parent = grafikYuvasi;
                grafik.Dock = DockStyle.Fill;
                grafik.BringToFront();
            }
        }

        private void pnlUnity_Resize(object sender, EventArgs e)
        {
            if (unityProcess != null && !unityProcess.HasExited)
            {
                MoveWindow(unityProcess.MainWindowHandle, 0, 0, pnlUnity.Width, pnlUnity.Height, true);
                ActivateUnityWindow();
            }
        }

        private void UnityGomVeBaslat()
        {
            try
            {
                string unityYolu = Path.Combine(appDir, "Similasyon", "LaviraSimulasyon.exe");
                if (!File.Exists(unityYolu))
                { MessageBox.Show("Unity Exe bulunamadı!\nBeklenen: " + unityYolu); return; }

                try
                {
                    foreach (var proc in Process.GetProcessesByName("LaviraSimulasyon"))
                    {
                        try
                        {
                            proc.CloseMainWindow();
                            if (!proc.WaitForExit(2000))
                                proc.Kill();
                            proc.WaitForExit(1000);
                            Debug.WriteLine($"Eski Unity process kapatıldı: PID {proc.Id}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Eski Unity kapatma hatası (PID {proc.Id}): {ex.Message}");
                        }
                    }
                    // Port serbest kalması için kısa bekleme
                    Thread.Sleep(500);
                }
                catch (Exception ex) { Debug.WriteLine("Eski process temizleme hatası: " + ex.Message); }

                ProcessStartInfo pInfo = new ProcessStartInfo(unityYolu)
                { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal };
                unityProcess = Process.Start(pInfo);

                unityProcess.WaitForInputIdle(5000);

                int pollingDenemesi = 0;
                const int maxPollingDenemesi = 50; // 50 x 100ms = 5 saniye
                while (unityProcess.MainWindowHandle == IntPtr.Zero && pollingDenemesi < maxPollingDenemesi)
                {
                    Thread.Sleep(100);
                    unityProcess.Refresh(); // Process önbelleğini tazele
                    pollingDenemesi++;
                }

                if (unityProcess.MainWindowHandle == IntPtr.Zero)
                {
                    Debug.WriteLine("Unity penceresi 5 saniye içinde açılamadı, gömme atlandı.");
                    return;
                }

                SetParent(unityProcess.MainWindowHandle, pnlUnity.Handle);
                SetWindowLong(unityProcess.MainWindowHandle, -16, 0x10000000);
                MoveWindow(unityProcess.MainWindowHandle, 0, 0, pnlUnity.Width, pnlUnity.Height, true);
                ShowWindow(unityProcess.MainWindowHandle, 5);

                // Unity'yi hemen aktive et
                ActivateUnityWindow();

                // Timer ile sürekli aktivasyon — Unity'nin donmasını önler
                unityKeepAliveTimer = new System.Windows.Forms.Timer();
                unityKeepAliveTimer.Interval = 100; // 100ms aralıkla
                unityKeepAliveTimer.Tick += UnityKeepAlive_Tick;
                unityKeepAliveTimer.Start();
            }
            catch (Exception ex) { Debug.WriteLine("Unity Başlatma Hatası: " + ex.Message); }
        }

        private void ActivateUnityWindow()
        {
            try
            {
                if (unityProcess == null || unityProcess.HasExited) return;
                IntPtr hwnd = unityProcess.MainWindowHandle;
                if (hwnd == IntPtr.Zero) return;

                // 1. Pencereyi aktif olarak işaretle
                SendMessage(hwnd, WM_ACTIVATE, (IntPtr)WA_ACTIVE, IntPtr.Zero);
                // 2. Non-client alanı (başlık çubuğu) aktive et
                SendMessage(hwnd, WM_NCACTIVATE, (IntPtr)1, IntPtr.Zero);
                // 3. Focus mesajı gönder
                PostMessage(hwnd, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);
                // 4. Yeniden çizilmesini zorla
                InvalidateRect(hwnd, IntPtr.Zero, false);
                UpdateWindow(hwnd);
            }
            catch (Exception ex) { Debug.WriteLine("Unity Activate Hatası: " + ex.Message); }
        }

        private void UnityKeepAlive_Tick(object sender, EventArgs e)
        {
            if (unityProcess == null || unityProcess.HasExited)
            {
                unityKeepAliveTimer?.Stop();
                return;
            }
            ActivateUnityWindow();
        }
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            ActivateUnityWindow();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                grafikAnimasyonTimer?.Stop();
                grafikAnimasyonTimer?.Dispose();

                // Unity keep-alive timer'ını durdur
                unityKeepAliveTimer?.Stop();
                unityKeepAliveTimer?.Dispose();

                AnaGovdeBaglantiKapat();
                GorevYukuBaglantiKapat();
                try
                {
                    Task[] beklenecekler = new[] { anaTask, gorevTask }
                        .Where(t => t != null && !t.IsCompleted)
                        .ToArray();
                    if (beklenecekler.Length > 0)
                        Task.WaitAll(beklenecekler, TimeSpan.FromSeconds(3));
                }
                catch (AggregateException aggEx)
                {
                    // Zaten iptal edilmiş task'ların OperationCanceledException'ları beklenen durum.
                    foreach (var ex in aggEx.InnerExceptions)
                        if (!(ex is OperationCanceledException))
                            Debug.WriteLine("Task kapanış hatası: " + ex.Message);
                }

                if (serialPortHakem.IsOpen) serialPortHakem.Close();

                lock (logKilidi)
                {
                    logWriter?.Close();
                    logWriter?.Dispose();
                }

                udpClient?.Close();
                durumSesiYoneticisi?.Dispose();
                if (unityProcess != null && !unityProcess.HasExited)
                {
                    unityProcess.CloseMainWindow();        // WM_CLOSE gönder
                    if (!unityProcess.WaitForExit(3000))   // 3 sn bekle
                    {
                        unityProcess.Kill();               // Hâlâ kapanmadıysa zorla öldür
                        unityProcess.WaitForExit(500);
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine("Kapanış Hatası: " + ex.Message); }

            base.OnFormClosing(e);
            Environment.Exit(0);
        }

    }
}