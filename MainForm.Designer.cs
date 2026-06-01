namespace AudioCompressor
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.SuspendLayout();

            // Form
            this.Text = "ضغط ملفات الصوت  ";
            this.Size = new System.Drawing.Size(1200, 780);
            this.MinimumSize = new System.Drawing.Size(900, 600);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.BackColor = System.Drawing.Color.FromArgb(10, 15, 25);
            this.ForeColor = System.Drawing.Color.FromArgb(200, 220, 240);
            this.Font = new System.Drawing.Font("Segoe UI", 9f);
            this.AllowDrop = true;

            this.ResumeLayout(false);
        }
    }
}
