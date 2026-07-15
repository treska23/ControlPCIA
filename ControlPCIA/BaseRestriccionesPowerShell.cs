namespace ControlPCIA
{
    internal static class BaseRestriccionesPowerShell
    {
        public const string Datos = """
        # =========================================================
        # FORMATO
        #
        # CMD      = nombre de comando completamente bloqueado
        # PREFIX   = comandos que comiencen por ese texto
        # ARG      = argumento peligroso en un comando concreto
        # TARGET   = destino que no puede lanzarse
        # TEXT     = fragmento peligroso presente en el código
        # SYNTAX   = construcción sintáctica bloqueada
        # =========================================================


        # =========================================================
        # BORRAR ARCHIVOS, CARPETAS O CONTENIDO
        # =========================================================

        CMD|Remove-Item
        CMD|Clear-Content
        CMD|Clear-Item

        # Aliases de Remove-Item
        CMD|del
        CMD|erase
        CMD|rd
        CMD|ri
        CMD|rm
        CMD|rmdir


        # =========================================================
        # CREAR O MODIFICAR ARCHIVOS Y CARPETAS
        # =========================================================

        CMD|New-Item
        CMD|Set-Content
        CMD|Add-Content
        CMD|Out-File
        CMD|Tee-Object

        CMD|Copy-Item
        CMD|Move-Item
        CMD|Rename-Item
        CMD|Set-Item

        # Aliases habituales
        CMD|ni
        CMD|md
        CMD|mkdir

        CMD|cp
        CMD|copy
        CMD|cpi

        CMD|mv
        CMD|move
        CMD|mi

        CMD|ren
        CMD|rni

        CMD|sc
        CMD|ac
        CMD|clc
        CMD|cli
        CMD|si


        # =========================================================
        # OPERACIONES DE ARCHIVOS INDIRECTAS
        # =========================================================

        CMD|Export-Csv
        CMD|Export-Clixml
        CMD|Export-Alias

        CMD|Import-Alias

        CMD|Compress-Archive
        CMD|Expand-Archive

        CMD|Start-Transcript
        CMD|Stop-Transcript

        CMD|Save-Module
        CMD|Save-Script

        CMD|robocopy
        CMD|xcopy
        CMD|mklink
        CMD|fsutil

        # Descargas que escriben directamente a disco
        ARG|Invoke-WebRequest|-OutFile
        ARG|Start-BitsTransfer|-Destination


        # =========================================================
        # ACCESO DIRECTO .NET / COM AL SISTEMA DE ARCHIVOS
        # =========================================================

        TEXT|System.IO.File
        TEXT|System.IO.Directory
        TEXT|[IO.File]
        TEXT|[IO.Directory]
        TEXT|FileInfo.Delete
        TEXT|DirectoryInfo.Delete
        TEXT|Scripting.FileSystemObject
        TEXT|ADODB.Stream


        # =========================================================
        # REDIRECCIONES QUE PUEDEN CREAR O MODIFICAR ARCHIVOS
        # =========================================================

        SYNTAX|>
        SYNTAX|>>


        # =========================================================
        # DISCOS, PARTICIONES Y VOLÚMENES
        # =========================================================

        CMD|Clear-Disk
        CMD|Initialize-Disk
        CMD|Format-Volume
        CMD|New-Partition
        CMD|Remove-Partition
        CMD|Resize-Partition

        CMD|Set-Disk
        CMD|Set-Partition
        CMD|Set-Volume

        CMD|Repair-Volume
        CMD|Optimize-Volume

        CMD|diskpart
        CMD|format
        CMD|mountvol
        CMD|chkdsk
        CMD|cipher


        # =========================================================
        # REGISTRO DE WINDOWS
        # =========================================================

        CMD|New-ItemProperty
        CMD|Set-ItemProperty
        CMD|Remove-ItemProperty
        CMD|Clear-ItemProperty
        CMD|Rename-ItemProperty
        CMD|Copy-ItemProperty
        CMD|Move-ItemProperty

        CMD|reg
        CMD|regedit

        TEXT|HKLM:
        TEXT|HKCU:
        TEXT|HKCR:
        TEXT|HKU:
        TEXT|HKCC:
        TEXT|Registry::


        # =========================================================
        # PERMISOS Y PROPIEDAD
        # =========================================================

        CMD|Set-Acl
        CMD|takeown
        CMD|icacls
        CMD|cacls
        CMD|attrib


        # =========================================================
        # USUARIOS Y GRUPOS DEL SISTEMA
        # =========================================================

        CMD|New-LocalUser
        CMD|Remove-LocalUser
        CMD|Set-LocalUser
        CMD|Enable-LocalUser
        CMD|Disable-LocalUser
        CMD|Rename-LocalUser

        CMD|New-LocalGroup
        CMD|Remove-LocalGroup
        CMD|Rename-LocalGroup

        CMD|Add-LocalGroupMember
        CMD|Remove-LocalGroupMember

        CMD|net

        TEXT|net user
        TEXT|net localgroup


        # =========================================================
        # SERVICIOS
        # =========================================================

        CMD|New-Service
        CMD|Set-Service
        CMD|Remove-Service

        CMD|sc.exe


        # =========================================================
        # TAREAS PROGRAMADAS
        # =========================================================

        CMD|Register-ScheduledTask
        CMD|Set-ScheduledTask
        CMD|Unregister-ScheduledTask
        CMD|New-ScheduledTask

        CMD|schtasks


        # =========================================================
        # RED Y FIREWALL
        # =========================================================

        PREFIX|Set-Net
        PREFIX|New-Net
        PREFIX|Remove-Net
        PREFIX|Disable-Net
        PREFIX|Enable-Net

        CMD|New-NetFirewallRule
        CMD|Set-NetFirewallRule
        CMD|Remove-NetFirewallRule

        CMD|netsh


        # =========================================================
        # SEGURIDAD / DEFENDER
        # =========================================================

        CMD|Set-MpPreference
        CMD|Add-MpPreference
        CMD|Remove-MpPreference

        CMD|Set-ExecutionPolicy


        # =========================================================
        # CONFIGURACIÓN DEL SISTEMA
        # =========================================================

        CMD|Rename-Computer
        CMD|Add-Computer
        CMD|Remove-Computer

        CMD|Restart-Computer
        CMD|Stop-Computer

        CMD|Set-Date
        CMD|Set-TimeZone

        CMD|Enable-WindowsOptionalFeature
        CMD|Disable-WindowsOptionalFeature

        CMD|Install-WindowsFeature
        CMD|Uninstall-WindowsFeature

        CMD|bcdedit
        CMD|bootsect
        CMD|reagentc
        CMD|shutdown


        # =========================================================
        # WMI / CIM CON CAPACIDAD DE MODIFICAR EL SISTEMA
        # =========================================================

        CMD|Set-CimInstance
        CMD|Remove-CimInstance
        CMD|Invoke-CimMethod

        CMD|Set-WmiInstance
        CMD|Remove-WmiObject
        CMD|Invoke-WmiMethod

        CMD|wmic


        # =========================================================
        # EJECUCIÓN DINÁMICA
        #
        # Se bloquea porque permitiría construir un comando
        # prohibido como texto y ejecutarlo después.
        # =========================================================

        CMD|Invoke-Expression
        CMD|iex

        CMD|Add-Type

        CMD|Invoke-Command
        CMD|icm


        # =========================================================
        # CREAR O MODIFICAR ALIASES
        #
        # Evita crear un alias nuevo para esconder un comando
        # que esté en la lista de restricciones.
        # =========================================================

        CMD|New-Alias
        CMD|Set-Alias
        CMD|Remove-Alias
        CMD|Import-Alias


        # =========================================================
        # EVITAR LANZAR OTRA CONSOLA PARA SALTARSE EL VALIDADOR
        # =========================================================

        CMD|powershell
        CMD|powershell.exe
        CMD|pwsh
        CMD|pwsh.exe
        CMD|cmd
        CMD|cmd.exe

        CMD|wscript
        CMD|wscript.exe
        CMD|cscript
        CMD|cscript.exe

        CMD|mshta
        CMD|mshta.exe

        CMD|rundll32
        CMD|rundll32.exe

        CMD|regsvr32
        CMD|regsvr32.exe


        # También cuando se intenten lanzar mediante Start-Process

        TARGET|powershell
        TARGET|powershell.exe
        TARGET|pwsh
        TARGET|pwsh.exe
        TARGET|cmd
        TARGET|cmd.exe
        TARGET|regedit
        TARGET|regedit.exe
        TARGET|diskpart
        TARGET|diskpart.exe

        ARG|Start-Process|-Verb RunAs


        # =========================================================
        # FIN
        # =========================================================
        """;
    }
}