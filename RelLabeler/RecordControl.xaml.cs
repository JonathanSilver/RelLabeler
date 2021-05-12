using System;
using System.Collections.Generic;
using System.Windows.Controls;

namespace RelLabeler
{
    /// <summary>
    /// RecordControl.xaml 的交互逻辑
    /// </summary>
    public partial class RecordControl : UserControl
    {
        public RecordControl(List<Tuple<string, string>> entityLabels, List<Tuple<string, string>> predicateLabels)
        {
            InitializeComponent();

            this.entityLabels = entityLabels;
            this.predicateLabels = predicateLabels;
            LoadLabels();
        }

        List<Tuple<string, string>> entityLabels;
        List<Tuple<string, string>> predicateLabels;

        private void ComboBoxLoad(ComboBox comboBox, List<Tuple<string, string>> labels)
        {
            comboBox.Items.Clear();
            foreach (var label in labels)
            {
                comboBox.Items.Add(LabelManager.GetLabelString(label));
            }
        }

        public void LoadLabels()
        {
            ComboBoxLoad(SubjectType, entityLabels);
            ComboBoxLoad(ObjectType, entityLabels);
            SubjectLabel = subjectType;
            ObjectLabel = objectType;
            ComboBoxLoad(PredicateType, predicateLabels);
            PredicateLabel = predicateType;
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
        private string predicateType;
        private string objectType;

        public string SubjectLabel
        {
            get { return subjectType; }
            set
            {
                subjectType = value;
                int pos = LabelManager.FindLabelIndexByName(entityLabels, value);
                SubjectType.SelectedIndex = pos;
            }
        }

        public string PredicateLabel
        {
            get { return predicateType; }
            set
            {
                predicateType = value;
                int pos = LabelManager.FindLabelIndexByName(predicateLabels, value);
                PredicateType.SelectedIndex = pos;
            }
        }

        public string ObjectLabel
        {
            get { return objectType; }
            set
            {
                objectType = value;
                int pos = LabelManager.FindLabelIndexByName(entityLabels, value);
                ObjectType.SelectedIndex = pos;
            }
        }

        private void SubjectType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SubjectType.SelectedIndex != -1)
            {
                subjectType = entityLabels[SubjectType.SelectedIndex].Item2;
            }
        }

        private void ObjectType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ObjectType.SelectedIndex != -1)
            {
                objectType = entityLabels[ObjectType.SelectedIndex].Item2;
            }
        }

        private void PredicateType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PredicateType.SelectedIndex != -1)
            {
                predicateType = predicateLabels[PredicateType.SelectedIndex].Item2;
            }
        }
    }
}
