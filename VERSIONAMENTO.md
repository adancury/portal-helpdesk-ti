# Versionamento do projeto

Este repositorio ja usa Git na pasta da solucao:

```powershell
C:\Users\adan.cury\source\repos\PortalHelpdeskTI
```

Nesta maquina, o Git disponivel foi encontrado pelo Visual Studio neste caminho:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe'
```

## Fluxo recomendado

Antes de iniciar um ajuste:

```powershell
git status
git pull
git checkout -b ajuste/nome-do-ajuste
```

Depois de testar:

```powershell
git status
git add .
git commit -m "Descreve o ajuste feito"
git push -u origin ajuste/nome-do-ajuste
```

Para marcar uma versao estavel:

```powershell
git tag -a v2026.05.27 -m "Versao estavel antes dos proximos ajustes"
git push origin v2026.05.27
```

## Como voltar para uma versao anterior

Ver historico:

```powershell
git log --oneline
```

Testar uma versao antiga sem apagar a atual:

```powershell
git checkout <hash-ou-tag>
```

Voltar para a branch principal:

```powershell
git checkout main
```

Desfazer o ultimo commit local, mantendo os arquivos alterados:

```powershell
git reset --soft HEAD~1
```

Reverter um commit ja enviado, criando um novo commit de correcao:

```powershell
git revert <hash-do-commit>
```

## Limpeza recomendada do controle de versao

Arquivos de Visual Studio, `bin` e `obj` nao devem ser versionados. O `.gitignore` ja foi configurado para impedir novos arquivos desse tipo, mas arquivos que ja estavam rastreados precisam ser removidos do indice do Git uma vez.

O comando abaixo nao apaga os arquivos da sua pasta; ele apenas para de versiona-los:

```powershell
git rm -r --cached .vs PortalHelpdeskTI/bin PortalHelpdeskTI/obj
git rm --cached PortalHelpdeskTI/PortalHelpdeskTI.csproj.user PortalHelpdeskTI/appsettings.Development.json
git commit -m "Ajusta arquivos ignorados do repositorio"
```

Revise `PortalHelpdeskTI/appsettings.json` antes de commitar. Se ele tiver senhas, connection strings reais, tokens ou chaves, o ideal e mover esses valores para `appsettings.Development.json`, variaveis de ambiente ou User Secrets.
