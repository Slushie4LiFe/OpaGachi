using System;
using System.Drawing;
using System.Windows.Forms;
namespace OpaGachi {
 sealed class ConfirmForm : GlassForm {
 readonly ToggleSwitch check=new ToggleSwitch();
 public bool Checked { get { return check.Checked; } }   // the optional switch, e.g. "Don't show this again"
 public ConfirmForm(string text, string confirm, string cancelText="Cancel", string checkText=null) {
 int y=checkText==null?128:162;
 Text="OpaGachi Confirmation"; ClientSize=new Size(470,y+52);
 var label=new Label { Text=text, Location=new Point(24,24), Size=new Size(422,80), ForeColor=Theme.TextPrimary, BackColor=Color.Transparent };
 var ok=new FlatButton(confirm,130) { Location=new Point(308,y) };
 var cancel=new FlatButton(cancelText,110) { Location=new Point(182,y) };
 ok.Click+=(s,e)=>{DialogResult=DialogResult.OK;Close();}; cancel.Click+=(s,e)=>{DialogResult=DialogResult.Cancel;Close();};
 Controls.AddRange(new Control[]{label,ok,cancel}); AcceptButton=null; CancelButton=null;
 if(checkText!=null) {
 check.Location=new Point(24,118);
 Controls.Add(check); Controls.Add(new Label { Text=checkText, AutoSize=true, Location=new Point(76,121), ForeColor=Theme.TextSecondary, BackColor=Color.Transparent });
 }
 KeyPreview=true; KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape){DialogResult=DialogResult.Cancel;Close();}};
 }
 }
}
