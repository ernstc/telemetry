using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common;

namespace EasySampleBlazorApp.Client.Pages
{
    public partial class Counter : ComponentBase
    {
        private int currentCount = 0;

        private void IncrementCount()
        {
            using (var sec = this.GetCodeSection())
            {
                IncrementCounterImpl();
            }
        }


        public int IncrementCounterImpl()
        {
            using (var sec = this.GetCodeSection())
            {
                currentCount++;

                sec.Result = currentCount;
                return currentCount;
            }
        }
    }
}
