using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Mir2.Admin.Services;

public sealed class ChineseMudLocalizer : MudLocalizer
{
    public override LocalizedString this[string key] => this[key, []];
    public override LocalizedString this[string key, params object[] arguments]
    {
        get
        {
            var text = key switch
            {
                "MudPagination_CurrentPage" => "当前第{0}页",
                "MudPagination_PageIndex" => "第{0}页",
                "MudPagination_FirstPage" or "MudTablePager_FirstPage" => "首页",
                "MudPagination_LastPage" or "MudTablePager_LastPage" => "末页",
                "MudPagination_NextPage" or "MudTablePager_NextPage" => "下一页",
                "MudPagination_PreviousPage" or "MudTablePager_PreviousPage" => "上一页",
                "MudDialog_Close" or "MudSnackbar_Close" => "关闭",
                "MudInput_Clear" => "清空",
                "MudInput_Decrement" => "减少",
                "MudInput_Increment" => "增加",
                "MudBaseDatePicker_PrevYear" => "上一年 {0}",
                "MudBaseDatePicker_NextYear" => "下一年 {0}",
                "MudBaseDatePicker_PrevMonth" => "上个月 {0}",
                "MudBaseDatePicker_NextMonth" => "下个月 {0}",
                "MudBaseDatePicker_Open" => "选择日期",
                "MudTimePicker_Open" => "选择时间",
                "MudAlert_Close" or "MudChip_Close" => "关闭",
                "MudNavGroup_ToggleExpand" => "展开或收起菜单",
                "Converter_InvalidBoolean" => "请输入有效的是或否。",
                "Converter_InvalidNumber" => "请输入有效数字。",
                "Converter_InvalidGUID" => "标识符格式无效。",
                "Converter_InvalidDateTime" => "日期或时间格式无效。",
                "Converter_InvalidTimeSpan" => "时长格式无效。",
                "Converter_NotValueOf" => "请输入有效的 {0} 值。",
                "Converter_InvalidType" or "Converter_ConversionNotImplemented" or "Converter_ConversionError" or "Converter_ConversionFailed" or "Converter_UnableToConvert" => "输入格式无效，请检查后重试。",
                "MudDataGridPager_RowsPerPage" => "每页条数：",
                "MudDataGridPager_AllItems" => "全部",
                "MudDataGridPager_InfoFormat" => "{0}–{1}，共{2}条",
                _ => null
            };
            return text == null ? base[key, arguments] : new(key, string.Format(text, arguments));
        }
    }
}
