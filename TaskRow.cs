using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TerminalWidget.Core;

namespace TerminalWidget;
// 제목을 포함한 넓은 영역을 하나의 체크 컨트롤로 묶어 클릭과 Space를 동일하게 처리한다.
public sealed class TaskRow : DockPanel
{
    public Guid TaskId {get;}
    private readonly CheckBox check;
    public void FocusCompletion()=>check.Focus();
    public TaskRow(TaskItem task, string metadata, Action<bool> complete, Action edit, Action delete)
    {
        TaskId=task.Id;
        Margin = new Thickness(0,3,0,3);
        var menuButton = Dialogs.Button("⋯", () => {});
        menuButton.Width=28;menuButton.Height=36;menuButton.VerticalAlignment=VerticalAlignment.Top;
        menuButton.ToolTip="수정 / 삭제";
        var menu=new ContextMenu();
        var editItem=new MenuItem {Header="할 일 수정"};editItem.Click+=(_,_)=>edit();menu.Items.Add(editItem);
        var deleteItem=new MenuItem {Header="할 일 삭제"};deleteItem.Click+=(_,_)=>delete();menu.Items.Add(deleteItem);
        menuButton.Click+=(_,_)=>{menu.PlacementTarget=menuButton;menu.IsOpen=true;};
        SetDock(menuButton,Dock.Right);Children.Add(menuButton);
        var labels=new StackPanel {Margin=new Thickness(0,3,0,3)};
        var title=new TextBlock {Text=task.Title,TextWrapping=TextWrapping.Wrap,FontSize=14,LineHeight=21,Foreground=new SolidColorBrush(task.CompletedAt==null?Color.FromRgb(221,225,228):Color.FromRgb(139,145,150))};
        if(task.CompletedAt!=null)title.TextDecorations=TextDecorations.Strikethrough;
        labels.Children.Add(title);
        if(!string.IsNullOrWhiteSpace(metadata))labels.Children.Add(new TextBlock {Text=metadata,TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=new SolidColorBrush(Color.FromRgb(146,153,160)),Margin=new Thickness(0,3,0,0)});
        check=new CheckBox {Content=labels,IsChecked=task.CompletedAt!=null,HorizontalContentAlignment=HorizontalAlignment.Stretch,MinHeight=38,Margin=new Thickness(0),ToolTip="클릭 또는 Space로 완료 전환"};
        System.Windows.Automation.AutomationProperties.SetName(check,task.Title);
        check.Click+=(_,_)=>complete(check.IsChecked==true);
        Children.Add(check);
    }
}
