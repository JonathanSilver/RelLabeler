using System.Collections.Generic;
using System.Windows.Controls;

namespace RelLabeler
{
    /// <summary>
    /// RecordControl.xaml 的交互逻辑
    /// </summary>
    public partial class RecordControl : UserControl
    {
        public RecordControl(List<string> entityLabels, List<string> predicateLabels)
        {
            InitializeComponent();

            this.entityLabels = entityLabels;
            this.predicateLabels = predicateLabels;
            LoadLabels();
        }

        List<string> entityLabels;
        List<string> predicateLabels;

        private void ComboBoxLoad(ComboBox comboBox, List<string> labels)
        {
            comboBox.Items.Clear();
            foreach (var label in labels)
            {
                comboBox.Items.Add(label);
            }
        }

        public void LoadLabels()
        {
            ComboBoxLoad(SubjectType, entityLabels);
            ComboBoxLoad(ObjectType, entityLabels);
            SubjectType.SelectedItem = subjectType;
            ObjectType.SelectedItem = objectType;
            ComboBoxLoad(PredicateText, predicateLabels);
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

        private string subjectType;
        private string objectType;

        public string SubjectLabel
        {
            get { return subjectType; }
            set
            {
                subjectType = value;
                SubjectType.SelectedItem = value;
            }
        }

        public string ObjectLabel
        {
            get { return objectType; }
            set
            {
                objectType = value;
                ObjectType.SelectedItem = value;
            }
        }

        private void SubjectType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SubjectType.SelectedItem != null)
            {
                subjectType = (string)SubjectType.SelectedItem;
            }
        }

        private void ObjectType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ObjectType.SelectedItem != null)
            {
                objectType = (string)ObjectType.SelectedItem;
            }
        }
    }
}
