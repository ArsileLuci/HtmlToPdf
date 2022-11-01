# HtmlToPdf
Weird looking thing that converts your HTML files to PDF

# Architecture
Default ASP .NET application based on .NET 6, to store data this app uses
EF Core + PostgreSQL. To maintain stabillity during heavy workloads it utilizes
background processes that do all heavy lifting simultaneously, the default
amount of concurrent background workers is set to `16`;

# Launching
The following steps must be executed before first start.
1) Install PostgreSQL.
2) Create new user called `app_user` with the permission to create database and pasword `app_user`
```
CREATE ROLE app_user WITH
  LOGIN
  NOSUPERUSER
  INHERIT
  CREATEDB
  NOCREATEROLE
  NOREPLICATION
  PASSWORD 'app_user';
```
Alternatively you can configure connection string the way you prefer.
3) Install Entity Framework Core Tools using `dotnet tool install --global dotnet-ef`.
4) Run all migrations with `dotnet ef database update`.
5) Run application with `dotnet run`.
6) After the project successfully launched navigate to `http://127.0.0.1:7777/HtmlConverter/`
