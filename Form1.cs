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
        private readonly object hakemKilit = new object();
        double RampaEnlem = 40.743336617541964, RampaBoylam = 29.941275119807784;
        bool RampaKaydedildi = false;
        bool RampaManuelAyarlandi = false;
        private readonly string appDir = AppDomain.CurrentDomain.BaseDirectory;
        private readonly TelemetriDurumu telemetriDurumu = new TelemetriDurumu();
        private DurumSesiYoneticisi durumSesiYoneticisi;
        private readonly byte[] anaBuffer = new byte[16384];
        private int anaBufferLen = 0;
        private readonly object anaBufferKilidi = new object();
        private BlockingCollection<byte[]> anaKuyruk;
        private CancellationTokenSource anaIptal;
        private Task anaTask; // OPT-6: Task referansı saklandı
        private readonly byte[] gorevBuffer = new byte[16384];
        private int gorevBufferLen = 0;
        private readonly object gorevBufferKilidi = new object();
        private BlockingCollection<byte[]> gorevKuyruk;
        private CancellationTokenSource gorevIptal;
        private Task gorevTask; // OPT-6: Task referansı saklandı
        private SynchronizationContext uiContext;
        private int paketSayaci = 0;
        private int gorevPaketSayaci = 0;
        private int sonHaritaGuncelleme = 0;
        private const int HaritaGuncellemeAraligi = 100; // ms
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
        }
        protected override void OnLoad(EventArgs e)
        {
            uiContext = SynchronizationContext.Current;
            base.OnLoad(e);
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            pnlUnity.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.Resize += (s, ev) => { pnlUnity_Resize(null, null); };
            panel1.MouseEnter += (s, ev) => panel1.Focus();
            tableLayoutPanel3.MouseEnter += (s, ev) => panel1.Focus();
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
                logFlushTimer = new System.Windows.Forms.Timer();
                logFlushTimer.Interval = 1000;
                logFlushTimer.Tick += (s, ev) => 
                {
                    lock (logKilidi)
                    {
                            logWriter?.Flush();                    }
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
                lblGorevMesafe.Text = "";
                lblGorevMesafe.Visible = false;
            }
            gMapControl1.MouseDoubleClick += gMapControl1_MouseDoubleClick;
            RampaHaritadaGuncelle();
        }

        private void btnBaglan_Click(object sender, EventArgs e)
        {
            if (!serialPort1.IsOpen)
            {
                if (string.IsNullOrWhiteSpace(cmbPorts.Text))
                { MessageBox.Show("Port seçmedin!"); return; }
                try
                {
                    serialPort1.PortName = cmbPorts.Text;
                    serialPort1.BaudRate = Convert.ToInt32(cmbBaud.Text);
                    serialPort1.DtrEnable = false; // ESP32/STM32 reset döngüsünü engeller
                    serialPort1.RtsEnable = false;
                    anaIptal = new CancellationTokenSource();
                    lock (anaBufferKilidi) { anaBufferLen = 0; }
                    anaKuyruk = new BlockingCollection<byte[]>(boundedCapacity: 500);
                    serialPort1.DataReceived += serialPort1_DataReceived;
                    serialPort1.Open();
                    anaTask = Task.Run(() => AnaKuyrukIsleyici(anaIptal.Token));
                    btnBaglan.Text = "KOPAR";
                    btnBaglan.BackColor = Color.Red;
                    btnBaglan.ForeColor = Color.White;
                }
                catch (Exception ex) { MessageBox.Show("Ana Port Hatası: " + ex.Message); }
            }
            else
            {
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
                anaIptal?.Cancel();
                serialPort1.DataReceived -= serialPort1_DataReceived;
                if (serialPort1.IsOpen) serialPort1.Close();
                anaKuyruk?.CompleteAdding();
                lock (anaBufferKilidi) { anaBufferLen = 0; }
            }
            catch (Exception ex) { Debug.WriteLine("Ana Bağlantı Kapama Hatası: " + ex.Message); }
        }
        private void btnGorevBaglan_Click(object sender, EventArgs e)
        {
            if (!serialPortGorev.IsOpen)
            {
                if (cmbGorevPort == null || string.IsNullOrWhiteSpace(cmbGorevPort.Text))
                { MessageBox.Show("Görev portu seçmedin!"); return; }

                try
                {
                    serialPortGorev.PortName = cmbGorevPort.Text;
                    serialPortGorev.BaudRate = Convert.ToInt32(cmbBaud.Text);
                    serialPortGorev.DtrEnable = false;
                    serialPortGorev.RtsEnable = false;
                    gorevIptal = new CancellationTokenSource();
                    lock (gorevBufferKilidi) { gorevBufferLen = 0; }
                    gorevKuyruk = new BlockingCollection<byte[]>(boundedCapacity: 500);
                    serialPortGorev.DataReceived += serialPortGorev_DataReceived;
                    serialPortGorev.Open();
                    gorevTask = Task.Run(() => GorevKuyrukIsleyici(gorevIptal.Token));
                    btnGorevBaglan.Text = "KOPAR";
                    btnGorevBaglan.BackColor = Color.Red;
                    btnGorevBaglan.ForeColor = Color.White;
                }
                catch (Exception ex) { MessageBox.Show("Görev Port Hatası: " + ex.Message); }
            }
            else
            {
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
                lock (gorevBufferKilidi) { gorevBufferLen = 0; }
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
                        MessageBox.Show("Ana port bağlantısı kesildi! (IO Hatası)", "Bağlantı Koptu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                        MessageBox.Show("Ana porta erişim reddedildi!", "Bağlantı Koptu",  MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }, null);
                    return;
                }

                if (bytesRead <= 0) return;

                lock (anaBufferKilidi)
                {
                    if (anaBufferLen + bytesRead > anaBuffer.Length)
                    {
                        Debug.WriteLine("ANA BYTE BUFFER TAŞTI, TEMİZLENDİ");
                        anaBufferLen = 0;
                    }

                    Buffer.BlockCopy(buffer, 0, anaBuffer, anaBufferLen, bytesRead);
                    anaBufferLen += bytesRead;

                    int readIndex = 0;
                    while (anaBufferLen - readIndex >= 61)
                    {
                        if (anaBuffer[readIndex] == 0xAB)
                        {
                            if (anaBuffer[readIndex + 59] == 0x0D && anaBuffer[readIndex + 60] == 0x0A)
                            {
                                uint sum = 0;
                                for (int i = 0; i < 58; i++) 
                                    sum += anaBuffer[readIndex + i];

                                if ((byte)(sum & 0xFF) == anaBuffer[readIndex + 58])
                                {
                                    byte[] paket = new byte[61];
                                    Buffer.BlockCopy(anaBuffer, readIndex, paket, 0, 61);
                                    if (anaKuyruk != null && !anaKuyruk.IsAddingCompleted)
                                    {
                                        if (!anaKuyruk.TryAdd(paket))
                                        {
                                            anaKuyruk.TryTake(out _);
                                            anaKuyruk.TryAdd(paket);
                                        }
                                    }

                                    readIndex += 61;
                                    continue;
                                }
                            }
                        }
                        readIndex++;
                    }

                    int kalan = anaBufferLen - readIndex;
                    if (kalan > 0 && readIndex > 0)
                    {
                        Buffer.BlockCopy(anaBuffer, readIndex, anaBuffer, 0, kalan);
                    }
                    anaBufferLen = kalan;
                }
            }
            catch (InvalidOperationException) { /* Kuyruk kapandı */ }
            catch (Exception ex) { Debug.WriteLine("Ana DataReceived Hatası: " + ex.Message); }
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
                        MessageBox.Show("Görev yükü portu bağlantısı kesildi! (IO Hatası)", "Bağlantı Koptu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                        MessageBox.Show("Görev yükü portuna erişim reddedildi!", "Bağlantı Koptu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }, null);
                    return;
                }

                if (bytesRead <= 0) return;

                lock (gorevBufferKilidi)
                {
                    if (gorevBufferLen + bytesRead > gorevBuffer.Length)
                    {
                        Debug.WriteLine("GÖREV BYTE BUFFER TAŞTI, TEMİZLENDİ");
                        gorevBufferLen = 0;
                    }

                    Buffer.BlockCopy(buffer, 0, gorevBuffer, gorevBufferLen, bytesRead);
                    gorevBufferLen += bytesRead;

                    int readIndex = 0;
                    while (gorevBufferLen - readIndex >= 35)
                    {
                        if (gorevBuffer[readIndex] == 0xAA && gorevBuffer[readIndex + 1] == 0x55)
                        {
                            byte xorHesap = 0;
                            for (int i = 2; i < 34; i++) 
                                xorHesap ^= gorevBuffer[readIndex + i];

                            if (xorHesap == gorevBuffer[readIndex + 34])
                            {
                                byte[] paket = new byte[35];
                                Buffer.BlockCopy(gorevBuffer, readIndex, paket, 0, 35);
                                if (gorevKuyruk != null && !gorevKuyruk.IsAddingCompleted)
                                {
                                    if (!gorevKuyruk.TryAdd(paket))
                                    {
                                        gorevKuyruk.TryTake(out _);
                                        gorevKuyruk.TryAdd(paket);
                                    }
                                }

                                readIndex += 35;
                                continue;
                            }
                        }
                        readIndex++;
                    }

                    int kalan = gorevBufferLen - readIndex;
                    if (kalan > 0 && readIndex > 0)
                    {
                        Buffer.BlockCopy(gorevBuffer, readIndex, gorevBuffer, 0, kalan);
                    }
                    gorevBufferLen = kalan;
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
                    try
                    {
                        if (b == null || b.Length != 61 || b[0] != 0xAB) continue;
                        if (b[59] != 0x0D || b[60] != 0x0A) continue;
                        uint sum = 0;
                        for (int i = 0; i < 58; i++) sum += b[i];
                        if ((byte)(sum & 0xFF) != b[58]) continue;
                        AnaGovdeVerisi v = new AnaGovdeVerisi
                        {
                            DurumKodu = b[1],
                            Irtifa = ReadFloatBE(b, 2),
                            Hiz = ReadFloatBE(b, 6),
                            Sicaklik = ReadFloatBE(b, 10),
                            GpsEnlem = ReadFloatBE(b, 14),
                            GpsBoylam = ReadFloatBE(b, 18),
                            Pitch = ReadFloatBE(b, 22),
                            Roll = ReadFloatBE(b, 26),
                            Yaw = ReadFloatBE(b, 30),
                            BasincMS = ReadFloatBE(b, 34),
                            BasincBMP = ReadFloatBE(b, 38),
                            BasincToplam = ReadFloatBE(b, 42),
                            IvmeX = ReadFloatBE(b, 46),
                            IvmeY = ReadFloatBE(b, 50),
                            IvmeZ = ReadFloatBE(b, 54),
                            Gecerli = true
                        };
                        Interlocked.Increment(ref paketSayaci);
                        telemetriDurumu.AnaGuncelle(v);
                        LogYaz("ANA");
                        UdpGonder(v);
                        HakemGonder();
                        uiContext?.Post(_ => AnaGovdeUIGuncelle(v), null);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Ana Paket Parse Hatası: " + ex.Message);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private void GorevKuyrukIsleyici(CancellationToken token)
        {
            try
            {
                foreach (byte[] b in gorevKuyruk.GetConsumingEnumerable(token))
                {
                    try
                    {
                        if (b == null || b.Length == 0) continue;
                        if (b.Length != 35 || b[0] != 0xAA || b[1] != 0x55)
                        {
                            continue;
                        }
                        byte xorHesap = 0;
                        for (int i = 2; i < 34; i++) xorHesap ^= b[i];
                        if (xorHesap != b[34])
                        {
                            Debug.WriteLine("[GÖREV KUYRUK XOR HATASI] Bozuk paket işlenmeden atıldı.");
                            continue;
                        }
                        GorevYukuVerisi v = new GorevYukuVerisi
                        {
                            HamIvmeX = BitConverter.ToSingle(b, 2),
                            FiltreX = BitConverter.ToSingle(b, 6),
                            HamIvmeY = BitConverter.ToSingle(b, 10),
                            FiltreY = BitConverter.ToSingle(b, 14),
                            HamIvmeZ = BitConverter.ToSingle(b, 18),
                            FiltreZ = BitConverter.ToSingle(b, 22),
                            GpsEnlem = BitConverter.ToSingle(b, 26),
                            GpsBoylam = BitConverter.ToSingle(b, 30),
                            Gecerli = true
                        };
                        Interlocked.Increment(ref gorevPaketSayaci);
                        telemetriDurumu.GorevGuncelle(v);
                        LogYaz("GOREV");
                        HakemGonder();
                        uiContext?.Post(_ => GorevYukuUIGuncelle(v), null);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Görev Paket Parse Hatası: " + ex.Message);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private void AnaGovdeUIGuncelle(AnaGovdeVerisi v)
        {
            try
            {
                lblDurum.Text = UcusDurumString(v.DurumKodu);
                durumSesiYoneticisi?.DurumGuncelle(v.DurumKodu);
                lblAnaIrtifa.Text = v.Irtifa.ToString("0.##", CultureInfo.InvariantCulture);
                lblAnaHiz.Text = v.Hiz.ToString("0.##", CultureInfo.InvariantCulture);
                if (lblAnaSicaklik != null) lblAnaSicaklik.Text = v.Sicaklik.ToString("0.##", CultureInfo.InvariantCulture);
                if (lblGpsEnlem != null) lblGpsEnlem.Text = v.GpsEnlem.ToString("F6", CultureInfo.InvariantCulture);
                if (lblGpsBoylam != null) lblGpsBoylam.Text = v.GpsBoylam.ToString("F6", CultureInfo.InvariantCulture);
                if (lblPitch != null) lblPitch.Text = v.Pitch.ToString("0.##", CultureInfo.InvariantCulture);
                if (lblRoll != null) lblRoll.Text = v.Roll.ToString("0.##", CultureInfo.InvariantCulture);
                if (lblYaw != null) lblYaw.Text = v.Yaw.ToString("0.##", CultureInfo.InvariantCulture);
                if (lblBasincMS != null) lblBasincMS.Text = v.BasincMS.ToString("0.##", CultureInfo.InvariantCulture);
                if (lblBasincBMP != null) lblBasincBMP.Text = v.BasincBMP.ToString("0.##", CultureInfo.InvariantCulture);
                if (lblBasincToplam != null) lblBasincToplam.Text = v.BasincToplam.ToString("0.##", CultureInfo.InvariantCulture);
                if (lblFiltreX != null) lblFiltreX.Text = v.IvmeX.ToString("0.##", CultureInfo.InvariantCulture);
                if (lblFiltreY != null) lblFiltreY.Text = v.IvmeY.ToString("0.##", CultureInfo.InvariantCulture);
                if (lblFiltreZ != null) lblFiltreZ.Text = v.IvmeZ.ToString("0.##", CultureInfo.InvariantCulture);
                lblPaket.Text = paketSayaci.ToString();
                hedefIrtifa = v.Irtifa;
                hedefHiz = v.Hiz;
                hedefSicaklik = v.Sicaklik;
                hedefBasinc = v.BasincToplam;
                hedefIvmeX = v.IvmeX;
                hedefIvmeY = v.IvmeY;
                hedefIvmeZ = v.IvmeZ;
                if (GpsGecerliMi(v.GpsEnlem, v.GpsBoylam))
                {
                    double lat = v.GpsEnlem;
                    double lng = v.GpsBoylam;

                    // Otomatik Rampa Kilidi (Auto-Home): İlk geçerli GPS geldiğinde rampayı roketin konumuna otomatik kilitler
                    if (!RampaManuelAyarlandi && (!RampaKaydedildi || (v.DurumKodu == 0 && HesaplaMesafe(RampaEnlem, RampaBoylam, lat, lng) > 41000)))
                    {
                        RampaEnlem = lat;
                        RampaBoylam = lng;
                        RampaKaydedildi = true;
                        RampaHaritadaGuncelle();
                    }

                    if (RampaKaydedildi)
                    {
                        double mesafe = HesaplaMesafe(RampaEnlem, RampaBoylam, lat, lng);
                        if (mesafe > 41000)
                        {
                            return; // 41 km'den uzak absürt koordinat reddedildi, harita güncellenmez!
                        }
                        lblMesafe.Visible = true;
                        lblMesafe.Text = "Mesafe: " + mesafe.ToString("F0") + " m";
                        lblMesafe.Location = new Point(15, 15); 
                        lblMesafe.BringToFront(); 

                        if (lblGorevMesafe != null)
                        {
                            if (v.DurumKodu < 5)
                            {
                                lblGorevMesafe.Visible = true;
                                lblGorevMesafe.Text = "GÖREV MESAFESİ: " + mesafe.ToString("F0") + " m";
                                lblGorevMesafe.Location = new Point(15, 45);
                                lblGorevMesafe.BringToFront();
                            }
                        }
                    }
                    roketIgnesi.Position = new PointLatLng(lat, lng);
                    if (v.DurumKodu < 5)
                    {
                        gorevIgnesi.Position = roketIgnesi.Position;
                        if (!gorevIgnesi.IsVisible) gorevIgnesi.IsVisible = true;
                        gorevYolu.Points.Clear();
                        if (RampaKaydedildi)
                        {
                            gorevYolu.Points.Add(new PointLatLng(RampaEnlem, RampaBoylam));
                            gorevYolu.Points.Add(new PointLatLng(lat, lng));
                        }
                        gMapControl1.UpdateRouteLocalPosition(gorevYolu);
                    }

                    roketYolu.Points.Clear();
                    if (RampaKaydedildi)
                    {
                        roketYolu.Points.Add(new PointLatLng(RampaEnlem, RampaBoylam));
                        roketYolu.Points.Add(new PointLatLng(lat, lng));
                    }
                    gMapControl1.UpdateRouteLocalPosition(roketYolu);
                    int simdi = Environment.TickCount;
                    if (simdi - sonHaritaGuncelleme >= HaritaGuncellemeAraligi)
                    {
                        sonHaritaGuncelleme = simdi;
                        gMapControl1.Refresh();
                    }
                }

            }
            catch (Exception ex) { Debug.WriteLine("AnaGovdeUI Hata: " + ex.Message); }
        }

        private void GorevYukuUIGuncelle(GorevYukuVerisi v)
        {
            try
            {
                if (lblGFiltreX != null) lblGFiltreX.Text = v.FiltreX.ToString("0.##", CultureInfo.InvariantCulture);
                if (lblGFiltreY != null) lblGFiltreY.Text = v.FiltreY.ToString("0.##", CultureInfo.InvariantCulture);
                if (lblGFiltreZ != null) lblGFiltreZ.Text = v.FiltreZ.ToString("0.##", CultureInfo.InvariantCulture);
                lblIvmeX.Text = v.HamIvmeX.ToString("0.##", CultureInfo.InvariantCulture);
                lblIvmeY.Text = v.HamIvmeY.ToString("0.##", CultureInfo.InvariantCulture);
                lblIvmeZ.Text = v.HamIvmeZ.ToString("0.##", CultureInfo.InvariantCulture);            
                if (lblGorevEnlem != null) lblGorevEnlem.Text = v.GpsEnlem.ToString("F6", CultureInfo.InvariantCulture);
                if (lblGorevBoylam != null) lblGorevBoylam.Text = v.GpsBoylam.ToString("F6", CultureInfo.InvariantCulture);
                if (lblGorevPaket != null) lblGorevPaket.Text = gorevPaketSayaci.ToString();
                if (GpsGecerliMi(v.GpsEnlem, v.GpsBoylam))
                {
                    double gLat = v.GpsEnlem;
                    double gLng = v.GpsBoylam;
                    if (RampaKaydedildi)
                    {
                        double gMesafe = HesaplaMesafe(RampaEnlem, RampaBoylam, gLat, gLng);
                        if (gMesafe > 41000)
                        {
                            return; // 41 km'den uzak absürt koordinat reddedildi!
                        }
                        gorevIgnesi.IsVisible = true;
                        lblGorevMesafe.Visible = true;
                        lblGorevMesafe.Text = "GÖREV MESAFESİ: " + gMesafe.ToString("F0") + " m";
                        lblGorevMesafe.Location = new Point(15, 45);
                        lblGorevMesafe.BringToFront();
                    }
                    else
                    {
                        gorevIgnesi.IsVisible = true;
                    }
                    gorevIgnesi.Position = new PointLatLng(gLat, gLng);

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

        private static bool GpsGecerliMi(double en, double bo)
        {
            if (double.IsNaN(en) || double.IsNaN(bo) || double.IsInfinity(en) || double.IsInfinity(bo))
                return false;
            if (Math.Abs(en) < 0.0001 && Math.Abs(bo) < 0.0001)
                return false;
            if (en < -90.0 || en > 90.0 || bo < -180.0 || bo > 180.0)
                return false;
            return true;
        }

        private static string CsvAlaniSarmalA(string deger)
        {
            if (deger == null) return "\"\"";
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
                        var (anaTam, gorevTam) = telemetriDurumu.ToStringSnapshot();
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

        private void UdpGonder(in AnaGovdeVerisi v)
        {
            try
            {
                string pitch = v.Pitch.ToString(CultureInfo.InvariantCulture);
                string roll = v.Roll.ToString(CultureInfo.InvariantCulture);
                string yaw = v.Yaw.ToString(CultureInfo.InvariantCulture);
                string pck = $"{pitch},{roll},{yaw},{v.DurumKodu}";
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
                var (ana, gorev) = telemetriDurumu.ToStringSnapshot();
                byte[] paket = HakemPaketleyici.PaketOlustur(ana, gorev);
                lock (hakemKilit)
                {
                    if (serialPortHakem.IsOpen)
                        serialPortHakem.Write(paket, 0, paket.Length);
                }
            }
            catch (Exception ex) { Debug.WriteLine("Hakem Gönderme Hatası: " + ex.Message); }
        }

        private string UcusDurumString(byte stateCode)
        {
            switch (stateCode)
            {
                case 0: return "0 - Bağlantı Kuruldu";
                case 1: return "1 - Yükseliyor";
                case 2: return "2 - Motor Yanma Sonu";
                case 3: return "3 - Süzülme";
                case 4: return "4 - 1. Ayrılma ";
                case 5: return "5 - 2. Ayrılma ";
                case 6: return "6 - İniş Yapıldı Tebrikler LAVİRA";
                default: return stateCode.ToString();
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
        private void OtomatikRampaKonumuAl()
        {
            var (ana, _) = telemetriDurumu.Snapshot();
            if (GpsGecerliMi(ana.GpsEnlem, ana.GpsBoylam))
            {
                double lat = ana.GpsEnlem;
                double lng = ana.GpsBoylam;
                RampaEnlem = lat;
                RampaBoylam = lng;
                RampaKaydedildi = true;
                RampaManuelAyarlandi = true;
                Debug.WriteLine($"Aviyonik GPS verisinden ayarlandı: {RampaEnlem}, {RampaBoylam}");
                MessageBox.Show($"Rampa konumu roketin mevcut GPS konumuna kilitlendi!\nEnlem: {lat}\nBoylam: {lng}", "Rampa Ayarlandı", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("Henüz geçerli bir roket GPS verisi ulaşmadı!\n(Enlem/Boylam 0 olarak görünüyor)\n\nRampa konumunu haritaya çift tıklayarak manuel de ayarlayabilirsiniz.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                if (roketIgnesi != null && paketSayaci == 0)
                {
                    roketIgnesi.Position = rampaKonum;
                }
                if (gorevIgnesi != null && gorevPaketSayaci == 0)
                {
                    gorevIgnesi.Position = rampaKonum;
                }
                if (roketIgnesi != null)
                {
                    double mesafe = HesaplaMesafe(RampaEnlem, RampaBoylam, roketIgnesi.Position.Lat, roketIgnesi.Position.Lng);
                    if (mesafe <= 41000)
                    {
                        lblMesafe.Visible = true;
                        lblMesafe.Text = "Mesafe: " + mesafe.ToString("F0") + " m";
                        lblMesafe.Location = new Point(15, 15); 
                        lblMesafe.BringToFront(); 
                        roketYolu.Points.Clear();
                        roketYolu.Points.Add(new PointLatLng(RampaEnlem, RampaBoylam));
                        roketYolu.Points.Add(new PointLatLng(roketIgnesi.Position.Lat, roketIgnesi.Position.Lng));
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
                        gorevYolu.Points.Clear();
                        gorevYolu.Points.Add(new PointLatLng(RampaEnlem, RampaBoylam));
                        gorevYolu.Points.Add(new PointLatLng(gorevIgnesi.Position.Lat, gorevIgnesi.Position.Lng));
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
        private void btnKonumSifirla_Click(object sender, EventArgs e)
        {
            btnKonumSifirla.Enabled = false;
            OtomatikRampaKonumuAl();
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
                RampaManuelAyarlandi = true;
                RampaHaritadaGuncelle();
                Debug.WriteLine($"[RAMPA MANUEL] Kullanıcı haritadan seçti: {lat}, {lng}");
            }
        }
        private void btnPortYenile_Click(object sender, EventArgs e)
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
                {
                    MessageBox.Show("Unity Exe bulunamadı!\nBeklenen: " + unityYolu, "Simülasyon Hatası", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Varsa eski çalışan simülasyonları kapat
                foreach (var proc in Process.GetProcessesByName("LaviraSimulasyon"))
                {
                    try
                    {
                        proc.CloseMainWindow();
                        if (!proc.WaitForExit(1000))
                            proc.Kill();
                    }
                    catch { }
                }

                ProcessStartInfo pInfo = new ProcessStartInfo(unityYolu)
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal
                };
                unityProcess = Process.Start(pInfo);
                unityProcess.WaitForInputIdle(5000);

                int pollingDenemesi = 0;
                const int maxPollingDenemesi = 50; // 50 x 100ms = 5 saniye
                while (unityProcess.MainWindowHandle == IntPtr.Zero && pollingDenemesi < maxPollingDenemesi)
                {
                    Thread.Sleep(100);
                    unityProcess.Refresh();
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
                ActivateUnityWindow(true);

                pnlUnity.MouseEnter -= PnlUnity_Focus;
                pnlUnity.MouseEnter += PnlUnity_Focus;
                pnlUnity.Click -= PnlUnity_Focus;
                pnlUnity.Click += PnlUnity_Focus;

                unityKeepAliveTimer = new System.Windows.Forms.Timer();
                unityKeepAliveTimer.Interval = 100;
                unityKeepAliveTimer.Tick += UnityKeepAlive_Tick;
                unityKeepAliveTimer.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Unity Başlatma Hatası: " + ex.Message);
            }
        }
        private void PnlUnity_Focus(object sender, EventArgs e)
        {
            ActivateUnityWindow(true);
        }
        private void RefreshUnityWindow()
        {
            if (unityProcess == null || unityProcess.HasExited) return;
            IntPtr hwnd = unityProcess.MainWindowHandle;
            if (hwnd == IntPtr.Zero) return;
            InvalidateRect(hwnd, IntPtr.Zero, false);
            UpdateWindow(hwnd);
        }

        private void ActivateUnityWindow(bool setFocus = false)
        {
            if (unityProcess == null || unityProcess.HasExited) return;
            IntPtr hwnd = unityProcess.MainWindowHandle;
            if (hwnd == IntPtr.Zero) return;
            SendMessage(hwnd, WM_ACTIVATE, (IntPtr)WA_ACTIVE, IntPtr.Zero);
            SendMessage(hwnd, WM_NCACTIVATE, (IntPtr)1, IntPtr.Zero);
            if (setFocus)
            {
                PostMessage(hwnd, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);
            }
            InvalidateRect(hwnd, IntPtr.Zero, false);
            UpdateWindow(hwnd);
        }
        private void UnityKeepAlive_Tick(object sender, EventArgs e)
        {
            if (unityProcess == null || unityProcess.HasExited)
            {
                unityKeepAliveTimer?.Stop();
                return;
            }
            RefreshUnityWindow();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            ActivateUnityWindow(false);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                grafikAnimasyonTimer?.Stop();
                grafikAnimasyonTimer?.Dispose();
                logFlushTimer?.Stop();
                logFlushTimer?.Dispose();
                unityKeepAliveTimer?.Stop();
                unityKeepAliveTimer?.Dispose();

                AnaGovdeBaglantiKapat();
                GorevYukuBaglantiKapat();

                Task[] beklenecekler = new[] { anaTask, gorevTask }
                    .Where(t => t != null && !t.IsCompleted)
                    .ToArray();
                if (beklenecekler.Length > 0)
                {
                    try { Task.WaitAll(beklenecekler, TimeSpan.FromSeconds(2)); } catch { }
                }

                if (serialPortHakem != null && serialPortHakem.IsOpen)
                {
                    try { serialPortHakem.Close(); } catch { }
                }

                if (unityProcess != null && !unityProcess.HasExited)
                {
                    try
                    {
                        unityProcess.CloseMainWindow();
                        if (!unityProcess.WaitForExit(1000))
                            unityProcess.Kill();
                    }
                    catch { }
                }

                udpClient?.Close();

                lock (logKilidi)
                {
                    try
                    {
                        logWriter?.Flush();
                        logWriter?.Close();
                        logWriter?.Dispose();
                    }
                    catch { }
                }

                durumSesiYoneticisi?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Kapanış Hatası: " + ex.Message);
            }
            base.OnFormClosing(e);
            Environment.Exit(0);
        }
    }
}