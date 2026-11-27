"""按钮声明约定检查，避免新页面重新混用默认尺寸和变体。"""
from pathlib import Path
import re

root=Path(__file__).resolve().parents[2]
count=0
for path in (root/'src/Admin/Components').rglob('*.razor'):
    text=path.read_text()
    for match in re.finditer(r'<(MudButton|MudIconButton)\b(?:[^">]|"[^"]*")*>',text,re.S):
        tag=match.group();count+=1
        required=['Size','Color']+(['Variant'] if match[1]=='MudButton' else ['aria-label'])
        for name in required:
            assert re.search(r'\b'+name+r'=',tag),f'{path.relative_to(root)}: 缺少 {name}: {tag}'
        if match[1]=='MudButton':
            expected='Size.Medium' if path.name=='Login.razor' else 'Size.Small'
            assert f'Size="{expected}"' in tag,f'{path}: 按钮尺寸不符合位置约定'
            before=text[:match.start()]
            if before.rfind('<RowTemplate')>before.rfind('</RowTemplate>'):
                assert 'Variant="Variant.Text"' in tag,f'{path}: 表格行操作必须使用文字按钮'
    for cell in re.finditer(r'<MudTd\b[^>]*>.*?</MudTd>',text,re.S):
        if re.search(r'<Mud(?:Icon)?Button\b',cell.group()):
            assert 'admin-row-actions' in cell.group().split('>',1)[0],f'{path}: 操作列缺少统一边框标记'
assert count>100,'未覆盖管理组件的全部按钮'
print(f'PASS {count} 个管理按钮/图标按钮的显式尺寸、变体、颜色及图标标签。')
