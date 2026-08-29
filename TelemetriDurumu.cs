namespace LaviraSON
{
    public class TelemetriDurumu
    {
        private readonly object _kilit = new object();

        public string[] AnaVeri { get; private set; }
        public string[] GorevVeri { get; private set; }

        public TelemetriDurumu()
        {
            AnaVeri = new string[27];
            for (int i = 0; i < AnaVeri.Length; i++) AnaVeri[i] = "0";

            GorevVeri = new string[10];
            for (int i = 0; i < GorevVeri.Length; i++) GorevVeri[i] = "0";
        }

        public void AnaGuncelle(string[] p)
        {
            lock (_kilit)
            {
                AnaVeri = p;
            }
        }

        public void GorevGuncelle(string[] p)
        {
            lock (_kilit)
            {
                GorevVeri = p;
            }
        }

        public (string[] ana, string[] gorev) Snapshot()
        {
            lock (_kilit)
            {
                return (
                    (string[])AnaVeri.Clone(),
                    (string[])GorevVeri.Clone()
                );
            }
        }
    }
}