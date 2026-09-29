# Исходные анимации (mocap)

Сюда кладутся исходные FBX-анимации (например, из Mixamo, скелет `mixamorig`, «Without Skin» или «With Skin»):

| Файл | Клип робота |
|---|---|
| `Standing_Idle.fbx` | Idle |
| `Walking.fbx` | Walk |
| `Running.fbx` | Run |
| `Crouching_Idle.fbx` | CrouchIdle |
| `Crouch_Walking.fbx` *(необязательно)* | CrouchWalk |

`Tools/Blender/build_robots.py` переносит их на скелет роботов (ретаргетинг) и запекает в `Robot_*.fbx`.
Если файла нет — используется процедурная анимация.

Сами FBX в репозиторий не коммитятся (`.gitignore`): по условиям Adobe анимации Mixamo можно
использовать в играх, но нельзя распространять как отдельные файлы.
