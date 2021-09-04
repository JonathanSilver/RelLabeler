using System;

namespace RelLabeler
{
    class Entity
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public Tuple<int, int> Position { get; set; }
    }
}
