using System.Windows;

namespace VnEmoteVoice.Console
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            ApplyLanguage();
        }

        public void ApplyLanguage()
        {
            Title = LocalizationManager.GetString("About_Title");
            LblAboutTitle.Text = LocalizationManager.GetString("About_AppTitle");
            LblVersion.Text = LocalizationManager.GetString("About_Version");
            LblAuthorLabel.Text = LocalizationManager.GetString("About_AuthorLabel") + " ";
            LblDescTitle.Text = LocalizationManager.GetString("About_DescTitle");
            TxtDesc1.Text = LocalizationManager.GetString("About_DescParagraph1");
            TxtDesc2.Text = LocalizationManager.GetString("About_DescParagraph2");
            LblLicense.Text = LocalizationManager.GetString("About_License");
            BtnClose.Content = LocalizationManager.GetString("About_BtnClose");
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
