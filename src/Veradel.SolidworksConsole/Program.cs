using SolidWorks.Interop.sldworks;
using System.Runtime.InteropServices;
using Veradel.SolidworksConsole.PartCreation;

namespace Veradel.SolidworksConsole
{
    public class Program
    {
        public static void Main(string[] args)
        {

            SldWorks swApp = GetSolidWorksApplication();

            BoltCreation create = new BoltCreation(swApp,30,5,20,30);

            create.CreateBolt();


        }


        // Get Solidworks session
        public static SldWorks GetSolidWorksApplication()
        {
            return (SldWorks)Marshal.GetActiveObject("SldWorks.Application");
        }

    }


}
