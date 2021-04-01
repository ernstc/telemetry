using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System;
using Common;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EasySampleBlazorv2.Client.Shared
{
    public partial class NavMenu : ComponentBase
    {
        [Inject]
        protected ILogger<NavMenu> _logger { get; set; }

        private bool collapseNavMenu = true;

        private string NavMenuCssClass => collapseNavMenu ? "collapse" : null;

        private void ToggleNavMenu()
        {
            using (var scope = _logger.BeginMethodScope())
            {
                collapseNavMenu = !collapseNavMenu;
            }
        }
    }
}
