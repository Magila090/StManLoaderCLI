using System;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

public static class GameRepair
{
    public static void RepairMenu()
    {
        Console.Clear();

        Console.WriteLine("╔════════════════════════════════════════════╗");
        Console.WriteLine("║              Исправление игры              ║");
        Console.WriteLine("╚════════════════════════════════════════════╝");
        Console.WriteLine();

        Console.Write("Введите путь к папке игры: ");

        string? gameDirectory = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            Console.WriteLine();
            Console.WriteLine("✗ Путь не указан.");
            Pause();
            return;
        }

        gameDirectory = Path.GetFullPath(gameDirectory.Trim());

        if (!Directory.Exists(gameDirectory))
        {
            Console.WriteLine();
            Console.WriteLine("✗ Папка игры не найдена:");
            Console.WriteLine(gameDirectory);
            Pause();
            return;
        }

        string[] dataDirectories =
            Directory.GetDirectories(
                gameDirectory,
                "*_Data",
                SearchOption.TopDirectoryOnly);

        if (dataDirectories.Length == 0)
        {
            Console.WriteLine();
            Console.WriteLine("✗ Папка *_Data не найдена.");
            Pause();
            return;
        }

        if (dataDirectories.Length > 1)
        {
            Console.WriteLine();
            Console.WriteLine(
                "✗ Найдено несколько папок *_Data:");

            foreach (string directory in dataDirectories)
            {
                Console.WriteLine(
                    $"  {Path.GetFileName(directory)}");
            }

            Console.WriteLine();
            Console.WriteLine(
                "Невозможно однозначно определить папку игры.");

            Pause();
            return;
        }

        string dataDirectory = dataDirectories[0];

        string managedDirectory =
            Path.Combine(
                dataDirectory,
                "Managed");

        if (!Directory.Exists(managedDirectory))
        {
            Console.WriteLine();
            Console.WriteLine("✗ Папка Managed не найдена:");
            Console.WriteLine(managedDirectory);
            Pause();
            return;
        }

        Console.WriteLine();
        Console.WriteLine(
            $"✓ Найдена папка игры: {Path.GetFileName(dataDirectory)}");

        Console.WriteLine(
            $"✓ Найдена папка Managed: {managedDirectory}");
        Console.WriteLine();

        string rlabrecquePath =
            Path.Combine(
                managedDirectory,
                "com.rlabrecque.steamworks.net.dll");

        string facepunchPath =
            Path.Combine(
                managedDirectory,
                "Facepunch.Steamworks.Win64.dll");

        bool foundSomething = false;

        // ============================================================
        // com.rlabrecque.steamworks.net.dll
        // ============================================================

        if (File.Exists(rlabrecquePath))
        {
            foundSomething = true;

            Console.WriteLine(
                "✓ Найден com.rlabrecque.steamworks.net.dll");

            Console.WriteLine();
            InspectRlabrecque(rlabrecquePath);

            Console.WriteLine();
            Console.Write(
                "Исправить? [Y/N]: ");

            string? answer = Console.ReadLine();

            if (answer?.Trim().Equals(
                    "Y",
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                Console.WriteLine();

                bool repaired =
                    RepairRlabrecque(rlabrecquePath);

                Console.WriteLine();

                if (repaired)
                {
                    Console.WriteLine(
                        "✓ Игра успешно исправлена.");
                }
                else
                {
                    Console.WriteLine(
                        "✗ Исправить игру не удалось.");
                }
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine(
                    "Исправление отменено.");
            }
        }

        // ============================================================
        // Facepunch.Steamworks.Win64.dll
        // ============================================================

        if (File.Exists(facepunchPath))
        {
            foundSomething = true;

            Console.WriteLine(
                "✓ Найден Facepunch.Steamworks.Win64.dll");

            Console.WriteLine();

            InspectFacepunch(
                managedDirectory,
                facepunchPath);

            Console.WriteLine();
            Console.Write(
                "Исправить? [Y/N]: ");

            string? answer = Console.ReadLine();

            if (answer?.Trim().Equals(
                "Y",
                StringComparison.OrdinalIgnoreCase) == true)
            {
                string[] dllNames =
                {
                    "Facepunch.Steamworks.Win64.dll",
                    "Facepunch Transport for Netcode for GameObjects.dll",
                    "Assembly-CSharp.dll"
                };

                foreach (string dllName in dllNames)
                {
                    string dllPath =
                        Path.Combine(
                            managedDirectory,
                            dllName);

                    if (!File.Exists(dllPath))
                        continue;

                    Console.WriteLine();

                    RepairFacepunch(
                        dllPath);
                }
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine(
                    "Исправление Facepunch отменено.");
            }
        }

        if (!foundSomething)
        {
            Console.WriteLine(
                "✗ Поддерживаемые Steamworks DLL не найдены.");

            Console.WriteLine();
            Console.WriteLine(
                "Искались:");

            Console.WriteLine(
                "  com.rlabrecque.steamworks.net.dll");

            Console.WriteLine(
                "  Facepunch.Steamworks.Win64.dll");
        }

        Console.WriteLine();
        Pause();
    }

    // ============================================================
    // RLABRECQUE
    // ============================================================

    private static void InspectRlabrecque(string dllPath)
    {
        try
        {
            ModuleDefMD module =
                ModuleDefMD.Load(dllPath);

            Console.WriteLine(
                "Ищем Steamworks.AppId_t(uint)...");

            TypeDef? appIdType = module.Types
                .FirstOrDefault(
                    type => type.FullName == "Steamworks.AppId_t");

            if (appIdType == null)
            {
                Console.WriteLine(
                    "✗ Steamworks.AppId_t не найден.");

                module.Dispose();
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Конструкторы AppId_t:");

            foreach (MethodDef method in appIdType.Methods)
            {
                if (method.Name == ".ctor")
                {
                    Console.WriteLine(
                        $"  {method.FullName}");
                }
            }

            MethodDef? constructor = appIdType.Methods
                .FirstOrDefault(
                    method =>
                        method.Name == ".ctor" &&
                        method.MethodSig != null &&
                        method.MethodSig.Params.Count == 1 &&
                        method.MethodSig.Params[0].ElementType == ElementType.U4);

            if (constructor == null)
            {
                Console.WriteLine(
                    "✗ Конструктор AppId_t(uint) не найден.");

                module.Dispose();
                return;
            }

            Console.WriteLine();
            Console.WriteLine(
                "✓ Найден Steamworks.AppId_t(uint)");

            Console.WriteLine();
            Console.WriteLine("IL:");

            foreach (Instruction instruction in
                    constructor.Body.Instructions)
            {
                Console.WriteLine(
                    $"  {instruction}");
            }

            module.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"✗ Ошибка анализа DLL: {ex.Message}");
        }
    }

    private static bool RepairRlabrecque(string dllPath)
    {
        ModuleDefMD? module = null;

        try
        {
            Console.WriteLine();
            Console.WriteLine("Исправляем AppId_t...");

            module = ModuleDefMD.Load(dllPath);

            TypeDef? appIdType = module.Types
                .FirstOrDefault(
                    type => type.FullName == "Steamworks.AppId_t");

            if (appIdType == null)
            {
                Console.WriteLine(
                    "✗ Steamworks.AppId_t не найден.");

                return false;
            }

            MethodDef? constructor = appIdType.Methods
                .FirstOrDefault(
                    method =>
                        method.Name == ".ctor" &&
                        method.MethodSig != null &&
                        method.MethodSig.Params.Count == 1 &&
                        method.MethodSig.Params[0].ElementType == ElementType.U4);

            if (constructor == null || !constructor.HasBody)
            {
                Console.WriteLine(
                    "✗ Конструктор AppId_t(uint) не найден.");

                return false;
            }

            Instruction? argumentLoad =
                constructor.Body.Instructions
                    .FirstOrDefault(
                        instruction =>
                            instruction.OpCode == OpCodes.Ldarg_1);

            if (argumentLoad == null)
            {
                Console.WriteLine(
                    "✗ Не найдена инструкция ldarg.1.");

                return false;
            }

            Console.WriteLine(
                "✓ Найдена инструкция ldarg.1.");

            argumentLoad.OpCode = OpCodes.Ldc_I4;
            argumentLoad.Operand = 480;

            Console.WriteLine(
                "✓ ldarg.1 заменена на ldc.i4 480.");

            string backupPath = dllPath + ".bak";
            string tempPath = dllPath + ".tmp";

            if (!File.Exists(backupPath))
            {
                File.Copy(
                    dllPath,
                    backupPath);

                Console.WriteLine(
                    $"✓ Создана резервная копия: {Path.GetFileName(backupPath)}");
            }

            Console.WriteLine(
                "Сохраняем изменённую DLL во временный файл...");

            module.Write(tempPath);

            // Очень важно:
            // закрываем исходную DLL до замены файла.
            module.Dispose();
            module = null;

            if (File.Exists(dllPath))
            {
                File.Delete(dllPath);
            }

            File.Move(
                tempPath,
                dllPath);

            Console.WriteLine(
                "✓ Изменённая DLL установлена.");

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"✗ Ошибка исправления: {ex.Message}");

            return false;
        }
        finally
        {
            if (module != null)
            {
                module.Dispose();
            }
        }
    }

    // ============================================================
    // FACEPUNCH
    // ============================================================

    private static void InspectFacepunch(
        string managedDirectory,
        string facepunchPath)
    {
        string[] dllNames =
        {
            "Facepunch.Steamworks.Win64.dll",
            "Facepunch Transport for Netcode for GameObjects.dll",
            "Assembly-CSharp.dll"
        };

        foreach (string dllName in dllNames)
        {
            string dllPath =
                Path.Combine(
                    managedDirectory,
                    dllName);

            Console.WriteLine();
            Console.WriteLine(
                $"Проверяем: {dllName}");

            if (!File.Exists(dllPath))
            {
                Console.WriteLine(
                    "  ✗ Файл отсутствует.");

                continue;
            }

            Console.WriteLine(
                "  ✓ Файл найден.");

            Console.WriteLine();
            Console.WriteLine(
                "========================================");

            Console.WriteLine(
                $"Анализ: {dllName}");

            Console.WriteLine(
                "========================================");

            InspectSteamClientInit(dllPath);
        }
    }

    private static void InspectSteamClientInit(string dllPath)
    {
        try
        {
            using ModuleDefMD module = ModuleDefMD.Load(dllPath);

            foreach (TypeDef type in module.GetTypes())
            {
                foreach (MethodDef method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;

                    foreach (Instruction instruction in method.Body.Instructions)
                    {
                        if (instruction.OpCode != OpCodes.Call &&
                            instruction.OpCode != OpCodes.Callvirt)
                            continue;

                        if (instruction.Operand is not IMethod calledMethod)
                            continue;

                        string? declaringType =
                            calledMethod.DeclaringType?.FullName;

                        if (calledMethod.Name != "Init")
                            continue;

                        if (declaringType != "Steamworks.SteamClient")
                            continue;

                        Console.WriteLine();
                        Console.WriteLine("✓ Найден SteamClient.Init:");

                        Console.WriteLine(
                            $"  Тип:    {type.FullName}");

                        Console.WriteLine(
                            $"  Метод:  {method.FullName}");

                        Console.WriteLine();

                        Console.WriteLine("  IL:");

                        foreach (Instruction il in method.Body.Instructions)
                        {
                            Console.WriteLine(
                                $"    {il}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"✗ Ошибка анализа DLL: {ex.Message}");
        }
    }

    private static bool TryReplaceFirstArgument(
        IList<Instruction> instructions,
        int callIndex)
    {
        if (callIndex < 2)
            return false;

        /*
        * SteamClient.Init(uint appId, bool asyncCallbacks)
        *
        * Перед CALL стек должен выглядеть так:
        *
        *     [appId]
        *     [asyncCallbacks]
        *
        * Последняя инструкция перед CALL формирует
        * второй аргумент.
        *
        * Идём назад и определяем границы
        * выражения первого аргумента по стеку.
        */

        int secondArgumentIndex = callIndex - 1;

        int stackBalance = 0;
        int firstArgumentStart = -1;

        for (int i = secondArgumentIndex - 1; i >= 0; i--)
        {
            Instruction instruction = instructions[i];

            int push;
            int pop;

            instruction.CalculateStackUsage(
                out push,
                out pop);

            stackBalance += push - pop;

            /*
            * Когда нашли значение, которое осталось
            * на стеке для первого аргумента —
            * нашли начало его выражения.
            */
            if (stackBalance > 0)
            {
                firstArgumentStart = i;
                break;
            }
        }

        if (firstArgumentStart < 0)
            return false;

        Console.WriteLine(
            $"  ✓ Первый аргумент найден: " +
            $"IL_{instructions[firstArgumentStart].Offset:X4}");

        Console.WriteLine(
            $"    до: {instructions[firstArgumentStart]}");

        /*
        * Ничего не удаляем!
        *
        * Первую инструкцию превращаем в:
        *
        *     ldc.i4 480
        *
        * Все остальные инструкции старого выражения
        * превращаем в NOP.
        *
        * Это сохраняет сами Instruction-объекты,
        * поэтому branch/exception-handler ссылки
        * не становятся висячими.
        */

        Instruction replacement =
            instructions[firstArgumentStart];

        replacement.OpCode = OpCodes.Ldc_I4;
        replacement.Operand = 480;

        for (int i = firstArgumentStart + 1;
            i < secondArgumentIndex;
            i++)
        {
            instructions[i].OpCode = OpCodes.Nop;
            instructions[i].Operand = null;
        }

        Console.WriteLine(
            "  ✓ Первый аргумент заменён на 480.");

        return true;
    }

    private static bool RepairFacepunch(
        string dllPath)
    {
        ModuleDefMD? module = null;

        try
        {
            Console.WriteLine();
            Console.WriteLine(
                $"Исправляем: {Path.GetFileName(dllPath)}");

            module = ModuleDefMD.Load(dllPath);

            int repairedCount = 0;

            foreach (TypeDef type in module.GetTypes())
            {
                foreach (MethodDef method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;

                    IList<Instruction> instructions =
                        method.Body.Instructions;

                    for (int i = 0; i < instructions.Count; i++)
                    {
                        Instruction instruction =
                            instructions[i];

                        if (instruction.OpCode != OpCodes.Call &&
                            instruction.OpCode != OpCodes.Callvirt)
                        {
                            continue;
                        }

                        if (instruction.Operand
                            is not IMethod calledMethod)
                        {
                            continue;
                        }

                        if (calledMethod.Name != "Init")
                            continue;

                        if (calledMethod.DeclaringType?.FullName !=
                            "Steamworks.SteamClient")
                        {
                            continue;
                        }

                        Console.WriteLine();
                        Console.WriteLine(
                            "✓ Найден SteamClient.Init:");

                        Console.WriteLine(
                            $"  Тип:   {type.FullName}");

                        Console.WriteLine(
                            $"  Метод: {method.FullName}");

                        if (TryReplaceFirstArgument(
                                instructions,
                                i))
                        {
                            repairedCount++;

                            Console.WriteLine(
                                "  ✓ Первый аргумент SteamClient.Init заменён на 480.");

                            continue;
                        }

                        Console.WriteLine(
                            "  ⚠ Не удалось определить первый аргумент.");
                    }
                }
            }

            if (repairedCount == 0)
            {
                Console.WriteLine();
                Console.WriteLine(
                    "⚠ Изменений не сделано.");

                module.Dispose();
                module = null;

                return false;
            }

            string backupPath =
                dllPath + ".bak";

            string tempPath =
                dllPath + ".tmp";

            if (!File.Exists(backupPath))
            {
                File.Copy(
                    dllPath,
                    backupPath);

                Console.WriteLine(
                    $"✓ Создана резервная копия: {Path.GetFileName(backupPath)}");
            }

            Console.WriteLine(
                $"✓ Исправлено вызовов: {repairedCount}");

            Console.WriteLine(
                "Сохраняем изменённую DLL во временный файл...");

            module.Write(tempPath);

            module.Dispose();
            module = null;

            if (File.Exists(dllPath))
            {
                File.Delete(dllPath);
            }

            File.Move(
                tempPath,
                dllPath);

            Console.WriteLine(
                "✓ Изменённая DLL установлена.");

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"✗ Ошибка исправления: {ex.Message}");

            return false;
        }
        finally
        {
            if (module != null)
            {
                module.Dispose();
            }
        }
    }

    // ============================================================
    // PAUSE
    // ============================================================

    private static void Pause()
    {
        Console.WriteLine();
        Console.WriteLine(
            "Нажмите Enter для продолжения...");

        Console.ReadLine();
    }
}