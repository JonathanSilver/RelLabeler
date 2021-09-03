using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media;

namespace RelLabeler
{
    /// <summary>
    /// RecordControl.xaml 的交互逻辑
    /// </summary>
    public partial class RecordControl : UserControl
    {
        public RecordControl(MainWindow mainWindow, List<Tuple<string, string>> entityLabels, List<Tuple<string, string>> predicateLabels, Record record)
        {
            InitializeComponent();

            this.mainWindow = mainWindow;

            this.entityLabels = entityLabels;
            this.predicateLabels = predicateLabels;

            this.record = record;
            record.Controls.Add(this);

            LoadLabels();

            Subject = record.Subject;
        }

        public readonly MainWindow mainWindow;
        readonly List<Tuple<string, string>> entityLabels;
        readonly List<Tuple<string, string>> predicateLabels;
        readonly Record record;

        public void SetAsAnnotation()
        {
            SubjectText.Foreground = Brushes.Orange;
            SubjectType.Foreground = Brushes.Orange;
            SubjectType.IsEnabled = false;
        }

        public void SetAsInvisible()
        {
            SubjectText.Foreground = Brushes.Red;
            SubjectType.Foreground = Brushes.Red;
            SubjectType.IsEnabled = false;
        }

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
            SubjectLabel = record.SubjectType;
        }

        public string Subject
        {
            get { return record.Subject; }
            set { record.Subject = value; }
        }

        public string SubjectLabel
        {
            get { return record.SubjectType; }
            set { record.SubjectType = value; }
        }

        public void SelectSubjectLabel(string label)
        {
            int pos = LabelManager.FindLabelIndexByName(entityLabels, label);
            SubjectType.SelectedIndex = pos;
        }

        private void SubjectText_TextChanged(object sender, TextChangedEventArgs e)
        {
            Subject = SubjectText.Text;
        }

        private void SubjectType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SubjectType.SelectedIndex != -1)
            {
                record.SubjectType = entityLabels[SubjectType.SelectedIndex].Item2;
            }
        }
    }
}
