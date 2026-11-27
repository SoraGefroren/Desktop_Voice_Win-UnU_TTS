using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace VnEmoteVoice.Console
{
    public partial class FindReplaceWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private bool _isReplaceMode;

        public FindReplaceWindow(MainWindow mainWindow, bool isReplaceMode = false)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            Owner = mainWindow;
            InitializeComponent();
            ApplyLanguage();
            SetMode(isReplaceMode);
        }

        public void ApplyLanguage()
        {
            Title = _isReplaceMode 
                ? LocalizationManager.GetString("FindReplace_Title_Replace") 
                : LocalizationManager.GetString("FindReplace_Title_Find");

            LblFindTitle.Text = LocalizationManager.GetString("FindReplace_LblFind");
            LblReplaceTitle.Text = LocalizationManager.GetString("FindReplace_LblReplace");
            ChkMatchCase.Content = LocalizationManager.GetString("FindReplace_ChkMatchCase");
            ChkWholeWord.Content = LocalizationManager.GetString("FindReplace_ChkWholeWord");
            LblDirectionTitle.Text = LocalizationManager.GetString("FindReplace_LblDirection");
            RbDown.Content = LocalizationManager.GetString("FindReplace_RbDown");
            RbUp.Content = LocalizationManager.GetString("FindReplace_RbUp");
            BtnFindNext.Content = LocalizationManager.GetString("FindReplace_BtnFindNext");
            BtnFindPrev.Content = LocalizationManager.GetString("FindReplace_BtnFindPrev");
            BtnReplace.Content = LocalizationManager.GetString("FindReplace_BtnReplace");
            BtnReplaceAll.Content = LocalizationManager.GetString("FindReplace_BtnReplaceAll");
            BtnClose.Content = LocalizationManager.GetString("FindReplace_BtnClose");
        }

        public void SetMode(bool isReplaceMode)
        {
            _isReplaceMode = isReplaceMode;
            Title = isReplaceMode 
                ? LocalizationManager.GetString("FindReplace_Title_Replace") 
                : LocalizationManager.GetString("FindReplace_Title_Find");
            LblStatus.Text = "";

            if (isReplaceMode)
            {
                TxtReplace.Focus();
                TxtReplace.SelectAll();
            }
            else
            {
                TxtFind.Focus();
                TxtFind.SelectAll();
            }
        }

        public string SearchText
        {
            get => TxtFind.Text;
            set
            {
                TxtFind.Text = value;
                TxtFind.SelectAll();
            }
        }

        public string ReplaceText => TxtReplace.Text;
        public bool MatchCase => ChkMatchCase.IsChecked == true;
        public bool WholeWord => ChkWholeWord.IsChecked == true;

        private void BtnFindNext_Click(object sender, RoutedEventArgs e)
        {
            ExecuteFind(searchDown: RbDown.IsChecked == true);
        }

        private void BtnFindPrev_Click(object sender, RoutedEventArgs e)
        {
            ExecuteFind(searchDown: false);
        }

        private void ExecuteFind(bool searchDown)
        {
            string query = TxtFind.Text;
            if (string.IsNullOrEmpty(query))
            {
                ShowStatus(LocalizationManager.GetString("FindReplace_Status_EnterText"), isError: true);
                return;
            }

            bool found = _mainWindow.FindTextInEditor(query, MatchCase, WholeWord, searchDown);
            if (!found)
            {
                ShowStatus(LocalizationManager.Format("FindReplace_Status_NotFound", query), isError: true);
            }
            else
            {
                ShowStatus(LocalizationManager.GetString("FindReplace_Status_Found"), isError: false);
            }
        }

        private void BtnReplace_Click(object sender, RoutedEventArgs e)
        {
            string query = TxtFind.Text;
            if (string.IsNullOrEmpty(query))
            {
                ShowStatus(LocalizationManager.GetString("FindReplace_Status_EnterText"), isError: true);
                return;
            }

            bool replaced = _mainWindow.ReplaceTextInEditor(query, TxtReplace.Text, MatchCase, WholeWord);
            if (!replaced)
            {
                ShowStatus(LocalizationManager.Format("FindReplace_Status_NotFound", query), isError: true);
            }
            else
            {
                ShowStatus(LocalizationManager.GetString("FindReplace_Status_Replaced"), isError: false);
            }
        }

        private void BtnReplaceAll_Click(object sender, RoutedEventArgs e)
        {
            string query = TxtFind.Text;
            if (string.IsNullOrEmpty(query))
            {
                ShowStatus(LocalizationManager.GetString("FindReplace_Status_EnterText"), isError: true);
                return;
            }

            int count = _mainWindow.ReplaceAllTextInEditor(query, TxtReplace.Text, MatchCase, WholeWord);
            if (count == 0)
            {
                ShowStatus(LocalizationManager.Format("FindReplace_Status_NoMatchesToReplace", query), isError: true);
            }
            else
            {
                ShowStatus(LocalizationManager.Format("FindReplace_Status_ReplacedAll", count), isError: false);
            }
        }

        private void ShowStatus(string message, bool isError)
        {
            LblStatus.Text = message;
            LblStatus.Foreground = isError
                ? new SolidColorBrush(Color.FromRgb(248, 113, 113)) // Rojo
                : new SolidColorBrush(Color.FromRgb(52, 211, 153));  // Verde
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void TxtFind_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnFindNext_Click(sender, e);
                e.Handled = true;
            }
        }

        private void TxtReplace_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnReplace_Click(sender, e);
                e.Handled = true;
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Ocultar en lugar de destruir la ventana para conservar los textos buscados
            e.Cancel = true;
            Hide();
        }
    }
}
