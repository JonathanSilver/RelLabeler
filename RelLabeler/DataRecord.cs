using System;

namespace RelLabeler
{
    class DataRecord
    {
        public string Item1 { get; set; } // subject
        public string Item2 { get; set; } // predicate
        public string Item3 { get; set; } // object
        public string Item4 { get; set; } // subject_type
        public string Item5 { get; set; } // object_type
        public string Item6 { get; set; } // predicate_type
        public Tuple<int, int> Item7 { get; set; } // position
        public bool? Item8 { get; set; } // annotated
    }
}
