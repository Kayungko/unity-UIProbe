using System;
using System.Collections.Generic;

namespace UIProbe
{
    [Serializable]
    public class ScanOptions
    {
        public List<string> TargetFolders = new List<string>();
        public List<string> ExcludeFolders = new List<string>();
        public bool IncludeSprites = true;
        public bool IncludeTextures = true;
        
        public bool CheckPrefabs = true;
        public bool CheckScenes = true;
        public bool CheckMaterials = true;
        public bool CheckAnimations = true;
        public bool CheckParticles = true;
        
        public bool UseCache = true;
    }
}
