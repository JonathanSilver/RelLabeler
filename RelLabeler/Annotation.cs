using System.Collections.Generic;

namespace RelLabeler
{
    class Annotation
    {
        public string FileName { get; set; }
        public int LineID { get; set; }

        public string Text { get; set; }
        public List<Entity> Entities { get; set; }
    }
}
