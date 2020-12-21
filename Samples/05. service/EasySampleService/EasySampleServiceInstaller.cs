using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.Linq;
using System.Threading.Tasks;

namespace EasySampleService
{
    [RunInstaller(true)]
    public partial class EasySampleServiceInstaller : System.Configuration.Install.Installer
    {
        public EasySampleServiceInstaller()
        {
            InitializeComponent();
        }
    }
}
