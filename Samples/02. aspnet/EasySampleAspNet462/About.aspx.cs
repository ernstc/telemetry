using Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace EasySampleAspNet462
{
    public partial class About : Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            //AssemblyResolver.ResolveToAssemblyReferences(typeof(AddAIPUserPermissions).Assembly);
            
            //TraceManager.Init(System.Diagnostics.SourceLevels.All, null);
            using (var sec = this.GetCodeSection()) {
            
            }
        }
    }
}