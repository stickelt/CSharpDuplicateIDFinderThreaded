using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DuplicateIDSearch
{
    public class SearchMatch
    {
        public string FileName { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string Match { get; set; }
    }
}
