using System;
using System.Collections.Generic;
using System.Windows;

namespace RelLabeler
{
    /// <summary>
    /// DictionaryManager.xaml 的交互逻辑
    /// </summary>
    public partial class DictionaryManager : Window
    {
        public DictionaryManager(MainWindow mainWindow, List<string> words, int type)
        {
            InitializeComponent();

            this.mainWindow = mainWindow;
            this.words = words;
            this.type = type;

            if (type == 0)
            {
                Title += " (Stopwords)";
            }
            else
            {
                Title += " (Hints)";
            }

            LoadWords();
        }

        readonly MainWindow mainWindow;
        readonly List<string> words;
        readonly int type;

        public void LoadWords()
        {
            WordList.Items.Clear();
            foreach (var word in words)
            {
                WordList.Items.Add(word);
            }
            WordList.SelectedItem = WordBox.Text;
        }

        private void AddWord_Click(object sender, RoutedEventArgs e)
        {
            mainWindow.AddWord(WordBox.Text, type);
        }

        private void RemoveWord_Click(object sender, RoutedEventArgs e)
        {
            mainWindow.RemoveWord(WordBox.Text, type);
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (type == 0)
            {
                mainWindow.stopwordsManager = null;
            }
            else
            {
                mainWindow.hintsManager = null;
            }
        }

        private void WordList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            WordBox.Text = (string)WordList.SelectedItem;
        }
    }
}
