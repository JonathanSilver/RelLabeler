using System.Windows.Controls;

namespace RelLabeler
{
    /// <summary>
    /// RecordControl.xaml 的交互逻辑
    /// </summary>
    public partial class RecordControl : UserControl
    {
        public RecordControl()
        {
            InitializeComponent();
        }

        public bool IsChecked
        {
            get { return (bool)IsSelected.IsChecked; }
            set { IsSelected.IsChecked = value; }
        }

        public string Subject
        {
            get { return SubjectText.Text; }
            set { SubjectText.Text = value; }
        }

        public string Predicate
        {
            get { return PredicateText.Text; }
            set { PredicateText.Text = value; }
        }

        public string Object
        {
            get { return ObjectText.Text; }
            set { ObjectText.Text = value; }
        }
    }
}
