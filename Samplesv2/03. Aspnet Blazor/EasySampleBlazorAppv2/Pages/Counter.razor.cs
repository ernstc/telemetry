using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Microsoft.Extensions.Logging;

namespace EasySampleBlazorAppv2.Pages
{
    public partial class Counter : ComponentBase
    {
        [Inject]
        protected ILogger<Counter> _logger { get; set; }

        private int currentCount = 0;

        private void IncrementCount()
        {
            using (var scope = _logger.BeginMethodScope())
            {
                IncrementCounterImpl();
            }
        }


        public int IncrementCounterImpl()
        {
            using (var scope = _logger.BeginMethodScope())
            {
                currentCount++;

                scope.Result = currentCount;
                return currentCount;
            }
        }
    }
}
