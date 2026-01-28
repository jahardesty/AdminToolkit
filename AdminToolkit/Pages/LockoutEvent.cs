using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AdminToolkit.Pages
{
    internal class LockoutEvent
    {
       
            public string Time { get; set; }
            public string UserName { get; set; } // Added this
            public string Source { get; set; }
            public string DC { get; set; }
        
    }
}
