using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Veradel.SolidworksConsole.Weldment
{
    public class WeldmentCreator
    {

        DrawingDoc _drawingDoc;
        SldWorks _swApp;


        public WeldmentCreator(ModelDoc2 model, SldWorks swApp)
        {
            _drawingDoc = (DrawingDoc)model;
            _swApp = swApp;
        }

        public void ExecuteCommand()
        {
            // open the part and create the weldmentfolder, note if the part has multiple configurations, there must be a form where the user can select the configuration

            Sheet activeSheet = _drawingDoc.GetCurrentSheet();
            object[] oViews = activeSheet.GetViews();

            View firstView = oViews[0] as View; // here I know that can crash if the currentSheet has no views

            ModelDoc2 referencedModel = firstView.ReferencedDocument;

            string docPath = referencedModel.GetPathName();

            int errors = 0, warnings = 0;
            _swApp.OpenDoc6(referencedModel.GetPathName(), swDocType(docPath), (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);


        }





        private int swDocType(string docPath)
        {
            string ext = Path.GetExtension(docPath).ToLower();

            switch (ext) {
                case ".sldprt": return (int)swDocumentTypes_e.swDocPART;
            }
            throw new Exception("NotSuported");
        }


    }
}
