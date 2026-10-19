using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace PrzPreview
{
    /// <summary>
    /// Einstieg für die native Brücke PrzShim.dll (MSIX-Paket). Die Brücke ruft diese Methode über
    /// ICLRRuntimeHost::ExecuteInDefaultAppDomain auf – deshalb die feste Signatur "static int M(string)".
    /// </summary>
    public static class ShimEntry
    {
        /// <param name="address">Hex-Adresse einer IUnknown*-Variablen der Brücke, in die der Handler geschrieben wird.</param>
        /// <returns>0 = Erfolg</returns>
        public static int Create(string address)
        {
            try
            {
                var handler = new PrzPreviewHandler();
                IntPtr unknown = Marshal.GetIUnknownForObject(handler);   // Referenz geht an die Brücke über
                Marshal.WriteIntPtr(new IntPtr(long.Parse(address, NumberStyles.HexNumber, CultureInfo.InvariantCulture)), unknown);
                return 0;
            }
            catch (Exception ex)
            {
                PrzPreviewHandler.Log("ShimEntry FEHLER: " + ex);
                return 1;
            }
        }
    }
}
