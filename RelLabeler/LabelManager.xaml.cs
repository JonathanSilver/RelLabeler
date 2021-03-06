using Microsoft.Data.Sqlite;
using System;
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
        public LabelManager(MainWindow mainWindow, string metaPath, List<Tuple<string, string>> labels, int type)
        {
            InitializeComponent();

            this.mainWindow = mainWindow;
            this.metaPath = metaPath;
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

            LoadLabels();
        }

        readonly MainWindow mainWindow;
        readonly string metaPath;
        readonly List<Tuple<string, string>> labels;
        readonly int type;

        public void LoadLabels()
        {
            LabelList.Items.Clear();
            foreach (var label in labels)
            {
                LabelList.Items.Add(GetLabelString(label));
            }
        }

        public static string GetLabelString(Tuple<string, string> label)
        {
            return label.Item1 + " - " + label.Item2;
        }

        public static int FindLabelIndexByCode(List<Tuple<string, string>> labels, string code)
        {
            return labels.FindIndex((x) => { return x.Item1 == code; });
        }

        public static int FindLabelIndexByName(List<Tuple<string, string>> labels, string name)
        {
            return labels.FindIndex((x) => { return x.Item2 == name; });
        }

        private void LabelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LabelList.SelectedIndex != -1)
            {
                LabelCode.Text = labels[LabelList.SelectedIndex].Item1;
                LabelName.Text = labels[LabelList.SelectedIndex].Item2;
            }
        }

        private void AddLabel_Click(object sender, RoutedEventArgs e)
        {
            string labelCode = LabelCode.Text;
            string labelName = LabelName.Text;
            if (labelCode != "" && labelName != ""
                && FindLabelIndexByCode(labels, labelCode) == -1 
                && FindLabelIndexByName(labels, labelName) == -1)
            {
                using (var connection = new SqliteConnection($"Data Source={metaPath}"))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        insert into label (code, name, type) values ($code, $name, $type);
                    ";
                    command.Parameters.AddWithValue("$code", labelCode);
                    command.Parameters.AddWithValue("$name", labelName);
                    command.Parameters.AddWithValue("$type", type);
                    command.ExecuteNonQuery();
                }
                mainWindow.LoadLabels();
                int pos = FindLabelIndexByCode(labels, labelCode);
                LabelList.SelectedIndex = pos;
            }
        }

        private void ChangeLabel_Click(object sender, RoutedEventArgs e)
        {
            string newLabelCode = LabelCode.Text;
            string newLabelName = LabelName.Text;
            int pos = LabelList.SelectedIndex;
            int newCodePos = FindLabelIndexByCode(labels, newLabelCode);
            int newNamePos = FindLabelIndexByName(labels, newLabelName);
            if (pos != -1 && newLabelCode != "" && newLabelName != ""
                && (newCodePos == -1 && newNamePos == -1
                || newCodePos == -1 && newNamePos == pos
                || newNamePos == -1 && newCodePos == pos))
            {
                string oldLabelCode = labels[pos].Item1;
                string oldLabelName = labels[pos].Item2;
                using (var connection = new SqliteConnection($"Data Source={metaPath}"))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        update label
                        set code = $newCode, name = $newName
                        where code = $oldCode and name = $oldName and type = $type;
                    ";
                    command.Parameters.AddWithValue("$newCode", newLabelCode);
                    command.Parameters.AddWithValue("$newName", newLabelName);
                    command.Parameters.AddWithValue("$oldCode", oldLabelCode);
                    command.Parameters.AddWithValue("$oldName", oldLabelName);
                    command.Parameters.AddWithValue("$type", type);
                    command.ExecuteNonQuery();
                }
                mainWindow.LoadLabels();
                pos = FindLabelIndexByCode(labels, newLabelCode);
                LabelList.SelectedIndex = pos;
            }
        }

        private void RemoveLabel_Click(object sender, RoutedEventArgs e)
        {
            int pos = LabelList.SelectedIndex;
            if (pos != -1)
            {
                string labelCode = labels[pos].Item1;
                string labelName = labels[pos].Item2;
                using (var connection = new SqliteConnection($"Data Source={metaPath}"))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        delete from label where code = $code and name = $name and type = $type;
                    ";
                    command.Parameters.AddWithValue("$code", labelCode);
                    command.Parameters.AddWithValue("$name", labelName);
                    command.Parameters.AddWithValue("$type", type);
                    command.ExecuteNonQuery();
                }
                mainWindow.LoadLabels();
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (type == 0)
            {
                mainWindow.entityLabelManager = null;
            }
        }
    }
}
