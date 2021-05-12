using Microsoft.Data.Sqlite;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace RelLabeler
{
    /// <summary>
    /// LabelManager.xaml 的交互逻辑
    /// </summary>
    public partial class LabelManager : Window
    {
        public LabelManager(string filePath, List<string> labels, int type)
        {
            InitializeComponent();

            this.filePath = filePath;
            this.labels = labels;
            this.type = type;

            if (type == 0)
            {
                Title += " (Entity)";
            }
            else
            {
                Title += " (Predicate)";
            }

            LabelList.Items.Clear();
            foreach (var label in labels)
            {
                LabelList.Items.Add(label);
            }
        }

        string filePath;
        List<string> labels;
        int type;

        private void LabelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LabelList.SelectedItem != null)
            {
                LabelName.Text = (string)LabelList.SelectedItem;
            }
        }

        private void AddLabel_Click(object sender, RoutedEventArgs e)
        {
            string labelName = LabelName.Text;
            if (labelName != "" && !labels.Contains(labelName))
            {
                labels.Add(labelName);
                LabelList.Items.Add(labelName);
                using (var connection = new SqliteConnection($"Data Source={filePath}"))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        insert into label values ($label, $type);
                    ";
                    command.Parameters.AddWithValue("$label", labelName);
                    command.Parameters.AddWithValue("$type", type);
                    command.ExecuteNonQuery();
                }
            }
        }

        private void ChangeLabel_Click(object sender, RoutedEventArgs e)
        {
            string newLabelName = LabelName.Text;
            string oldLabelName = (string)LabelList.SelectedItem;
            if (oldLabelName != null && !labels.Contains(newLabelName))
            {
                int pos = labels.FindIndex((x) => { return x == oldLabelName; });
                labels[pos] = newLabelName;
                LabelList.Items[pos] = newLabelName;
                using (var connection = new SqliteConnection($"Data Source={filePath}"))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        update label set name = $newLabel where name = $oldLabel and type = $type;
                    ";
                    command.Parameters.AddWithValue("$newLabel", newLabelName);
                    command.Parameters.AddWithValue("$oldLabel", oldLabelName);
                    command.Parameters.AddWithValue("$type", type);
                    command.ExecuteNonQuery();
                }
            }
        }

        private void RemoveLabel_Click(object sender, RoutedEventArgs e)
        {
            string labelName = (string)LabelList.SelectedItem;
            if (labelName != null)
            {
                labels.Remove(labelName);
                LabelList.Items.Remove(labelName);
                using (var connection = new SqliteConnection($"Data Source={filePath}"))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        delete from label where name = $label and type = $type;
                    ";
                    command.Parameters.AddWithValue("$label", labelName);
                    command.Parameters.AddWithValue("$type", type);
                    command.ExecuteNonQuery();
                }
            }
        }
    }
}
