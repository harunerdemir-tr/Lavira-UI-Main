using System;
using System.Globalization;

namespace LaviraSON
{
    public struct AnaGovdeVerisi
    {
        public byte DurumKodu;
        public float Irtifa;
        public float Hiz;
        public float Sicaklik;
        public float GpsEnlem;
        public float GpsBoylam;
        public float Pitch;
        public float Roll;
        public float Yaw;
        public float BasincMS;
        public float BasincBMP;
        public float BasincToplam;
        public float IvmeX;
        public float IvmeY;
        public float IvmeZ;
        public bool Gecerli;
    }

    public struct GorevYukuVerisi
    {
        public float FiltreX;
        public float FiltreY;
        public float FiltreZ;
        public float HamIvmeX;
        public float HamIvmeY;
        public float HamIvmeZ;
        public float GpsEnlem;
        public float GpsBoylam;
        public bool Gecerli;
    }

    public class TelemetriDurumu
    {
        private readonly object _kilit = new object();

        public AnaGovdeVerisi AnaVeri { get; private set; }
        public GorevYukuVerisi GorevVeri { get; private set; }

        public void AnaGuncelle(in AnaGovdeVerisi v)
        {
            lock (_kilit)
            {
                AnaVeri = v;
            }
        }

        public void GorevGuncelle(in GorevYukuVerisi v)
        {
            lock (_kilit)
            {
                GorevVeri = v;
            }
        }

        public (AnaGovdeVerisi ana, GorevYukuVerisi gorev) Snapshot()
        {
            lock (_kilit)
            {
                return (AnaVeri, GorevVeri);
            }
        }

        // HakemPaketleyici ve geriye dönük string bekleyen fonksiyonlar için %100 uyumlu dönüştürücü
        public (string[] ana, string[] gorev) ToStringSnapshot()
        {
            AnaGovdeVerisi a;
            GorevYukuVerisi g;
            lock (_kilit)
            {
                a = AnaVeri;
                g = GorevVeri;
            }

            string[] anaDizi = new string[27];
            for (int i = 0; i < anaDizi.Length; i++) anaDizi[i] = "0";

            if (a.Gecerli)
            {
                anaDizi[0] = a.DurumKodu.ToString();
                anaDizi[1] = a.Irtifa.ToString(CultureInfo.InvariantCulture);
                anaDizi[2] = a.Hiz.ToString(CultureInfo.InvariantCulture);
                anaDizi[3] = a.Sicaklik.ToString(CultureInfo.InvariantCulture);
                anaDizi[5] = a.GpsEnlem.ToString(CultureInfo.InvariantCulture);
                anaDizi[6] = a.GpsBoylam.ToString(CultureInfo.InvariantCulture);
                anaDizi[7] = a.Pitch.ToString(CultureInfo.InvariantCulture);
                anaDizi[8] = a.Roll.ToString(CultureInfo.InvariantCulture);
                anaDizi[9] = a.Yaw.ToString(CultureInfo.InvariantCulture);
                anaDizi[10] = a.BasincMS.ToString(CultureInfo.InvariantCulture);
                anaDizi[11] = a.BasincBMP.ToString(CultureInfo.InvariantCulture);
                anaDizi[12] = a.BasincToplam.ToString(CultureInfo.InvariantCulture);
                anaDizi[13] = a.Irtifa.ToString(CultureInfo.InvariantCulture);
                anaDizi[20] = a.IvmeX.ToString(CultureInfo.InvariantCulture);
                anaDizi[21] = a.IvmeY.ToString(CultureInfo.InvariantCulture);
                anaDizi[22] = a.IvmeZ.ToString(CultureInfo.InvariantCulture);
                anaDizi[23] = a.IvmeX.ToString(CultureInfo.InvariantCulture);
                anaDizi[24] = a.IvmeY.ToString(CultureInfo.InvariantCulture);
                anaDizi[25] = a.IvmeZ.ToString(CultureInfo.InvariantCulture);
            }

            string[] gorevDizi = new string[10];
            for (int i = 0; i < gorevDizi.Length; i++) gorevDizi[i] = "0";

            if (g.Gecerli)
            {
                gorevDizi[0] = g.FiltreX.ToString(CultureInfo.InvariantCulture);
                gorevDizi[1] = g.FiltreY.ToString(CultureInfo.InvariantCulture);
                gorevDizi[2] = g.FiltreZ.ToString(CultureInfo.InvariantCulture);
                gorevDizi[3] = g.HamIvmeX.ToString(CultureInfo.InvariantCulture);
                gorevDizi[4] = g.HamIvmeY.ToString(CultureInfo.InvariantCulture);
                gorevDizi[5] = g.HamIvmeZ.ToString(CultureInfo.InvariantCulture);
                gorevDizi[6] = g.GpsEnlem.ToString(CultureInfo.InvariantCulture);
                gorevDizi[7] = g.GpsBoylam.ToString(CultureInfo.InvariantCulture);
            }

            return (anaDizi, gorevDizi);
        }
    }
}