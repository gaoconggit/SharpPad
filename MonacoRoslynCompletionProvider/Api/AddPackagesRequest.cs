﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MonacoRoslynCompletionProvider.Api
{
    public class AddPackagesRequest
    {
        public List<Package> Packages { get; set; }
    }
}