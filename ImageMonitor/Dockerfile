FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build-env
WORKDIR /app
COPY *.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o out
FROM mcr.microsoft.com/dotnet/core/aspnet:3.1 AS runtime
ENV ASPNETCORE_URLS=http://+:8080 
WORKDIR /app
EXPOSE 8080/tcp
COPY --from=build-env /app/out .
ENTRYPOINT ["dotnet", "Monitor.dll"]