﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TeamsGenerator.API
{
    public class EditableInClientAttribute : Attribute
    {
        public bool Show { get; set; }
    }
}
