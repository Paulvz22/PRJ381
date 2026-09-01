using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace BCOpendayApp.Models;
public class MenuItem : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _icon = string.Empty;
    private string _route = string.Empty;
    private Color _iconColor = Colors.White;

    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }
    public string Icon { get => _icon; set { _icon = value; OnPropertyChanged(); } }
    public string Route { get => _route; set { _route = value; OnPropertyChanged(); } }
    public Color IconColor { get => _iconColor; set { _iconColor = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string propertyName = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
