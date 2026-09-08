using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading.Tasks;
using TerminalWidget.Core;

namespace TerminalWidget;
public static class ProjectEditor
{
    public static Project? Show(Project? existing)
    {
        var w=Dialogs.Form(existing==null?"프로젝트 추가":"프로젝트 수정",out var panel);w.Width=410;
        w.Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        Dialogs.Label(panel,"프로젝트 이름");var name=new TextBox {Text=existing?.Name??"",MaxLength=200};panel.Children.Add(name);
        Dialogs.Label(panel,"설명");var description=new TextBox {Text=existing?.Description??"",MaxLength=10000,Height=55,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap};panel.Children.Add(description);
        Dialogs.Label(panel,"상태");var status=new ComboBox {ItemsSource=Rules.Statuses,SelectedItem=existing?.Status??"진행 중"};panel.Children.Add(status);
        Dialogs.Label(panel,"진행률 방식");var mode=new ComboBox {ItemsSource=Rules.ProgressModes,SelectedItem=existing?.ProgressMode??"폴더 자동"};panel.Children.Add(mode);
        var folderPanel=new StackPanel();panel.Children.Add(folderPanel);
        string? folder=existing?.FolderPath;
        var pathLabel=new TextBlock {Text=folder??"연결된 폴더 없음",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,6)};
        folderPanel.Children.Add(pathLabel);
        var select=Dialogs.Button("프로젝트 폴더 선택",()=>{});folderPanel.Children.Add(select);
        Dialogs.Label(folderPanel,"완료 기준 파일 (선택한 폴더 바로 아래)");
        var files=new ComboBox {ItemsSource=new[]{"자동 판별"}.Concat(FolderProgress.Files).ToArray(),SelectedItem=existing?.PlanFile??"자동 판별"};folderPanel.Children.Add(files);
        var info=new TextBlock {Text="- [x] 완료 / - [ ] 미완료\n큰 단계 아래의 최하위 작업만 같은 비중으로 계산합니다. 코드·대화로 완성도를 추측하지 않습니다.",TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(0,8,0,8)};folderPanel.Children.Add(info);
        var manualPanel=new StackPanel();panel.Children.Add(manualPanel);
        Dialogs.Label(manualPanel,"진행률 (0–100%)");var slider=new Slider {Minimum=0,Maximum=100,TickFrequency=1,IsSnapToTickEnabled=true,Value=existing?.ManualPercent??0};manualPanel.Children.Add(slider);
        var number=new TextBox {Text=((int)slider.Value).ToString(),MaxLength=3};manualPanel.Children.Add(number);
        slider.ValueChanged+=(_,_)=>number.Text=((int)slider.Value).ToString();number.LostKeyboardFocus+=(_,_)=>{if(int.TryParse(number.Text,out int n)&&n>=0&&n<=100)slider.Value=n;};
        var preview=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};panel.Children.Add(preview);
        ProgressSnapshot? snapshot=existing?.Snapshot;bool busy=false;Project? result=null;
        Project Draft()=>new(existing?.Id??Guid.NewGuid(),name.Text.Trim(),description.Text.Trim(),(string)status.SelectedItem,(string)mode.SelectedItem,(int)slider.Value,folder,files.SelectedIndex==0?null:(string)files.SelectedItem,snapshot);
        bool SameSource()=>existing!=null&&existing.ProgressMode==(string)mode.SelectedItem&&existing.FolderPath==folder&&existing.PlanFile==(files.SelectedIndex==0?null:(string)files.SelectedItem);
        void Update(){folderPanel.Visibility=(string)mode.SelectedItem=="폴더 자동"?Visibility.Visible:Visibility.Collapsed;manualPanel.Visibility=(string)mode.SelectedItem=="수동"?Visibility.Visible:Visibility.Collapsed;snapshot=SameSource()?existing?.Snapshot:null;preview.Text=SameSource()?"저장된 결과를 유지합니다. 다시 읽으려면 위젯의 ↻를 누르세요.":"폴더를 연결하면 기준 확인 후 등록할 수 있습니다.";}
        select.Click+=(_,_)=>{var picker=new Microsoft.Win32.OpenFolderDialog {Title="프로젝트 루트 폴더 선택",Multiselect=false};if(picker.ShowDialog(w)==true){folder=picker.FolderName;pathLabel.Text=folder;Update();}};
        mode.SelectionChanged+=(_,_)=>Update();files.SelectionChanged+=(_,_)=>Update();
        var save=Dialogs.Button("저장",()=>{});panel.Children.Add(save);
        save.Click+=async (_,_)=>
        {
            if(busy)return;
            if(string.IsNullOrWhiteSpace(name.Text)){name.Focus();return;}
            if((string)mode.SelectedItem=="수동"){if(!int.TryParse(number.Text,out int n)||n<0||n>100){preview.Text="0부터 100 사이의 정수를 입력하세요.";return;}slider.Value=n;}
            if((string)mode.SelectedItem=="폴더 자동"&&string.IsNullOrWhiteSpace(folder)){preview.Text="먼저 프로젝트 폴더를 선택하세요.";return;}
            if((string)mode.SelectedItem=="폴더 자동"&&!SameSource())
            {
                busy=true;panel.IsEnabled=false;preview.Text="작업 기준 확인 중…";
                var draft=Draft() with {Snapshot=null};
                try {snapshot=await Task.Run(()=>FolderProgress.Read(draft));}
                finally{busy=false;panel.IsEnabled=true;}
                if(!w.IsVisible)return;
                string message=snapshot.Error??$"{snapshot.Source}\n최하위 작업 {snapshot.Total}개 중 {snapshot.Done}개 완료 → {snapshot.Percent}%";
                if(MessageBox.Show(w,message+"\n\n이 기준으로 프로젝트를 등록할까요?","연결 결과 확인",MessageBoxButton.YesNo)!=MessageBoxResult.Yes){preview.Text=message;return;}
            }
            result=Draft();w.DialogResult=true;
        };
        w.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape&&!busy)w.Close();};w.Closing+=(_,e)=>{if(busy)e.Cancel=true;};Update();w.ShowDialog();return result;
    }
}
