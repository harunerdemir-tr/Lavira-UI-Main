namespace LaviraSON
{
    public class TelemetriDurumu
    {
        private readonly object _kilit = new object();

        public string[] AnaVeri { get; private set; } = new string[27];
        public string[] GorevVeri { get; private set; } = new string[10];

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