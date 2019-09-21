﻿using System.Collections.Generic;

namespace Celeste.Mod.UpdateChecker
{
    class ModUpdateInfo {
        public virtual string Name { get; set; }
        public virtual string Version { get; set; }
        public virtual int LastUpdate { get; set; }
        public virtual string URL { get; set; }
        public virtual List<string> xxHash { get; set; }
    }
}
