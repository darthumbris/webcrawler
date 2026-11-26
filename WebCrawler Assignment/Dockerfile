FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
RUN apt-get update \
	&& apt-get install --no-install-recommends -y curl \
	&& rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["WebCrawler Assignment/WebCrawler.csproj", "WebCrawler Assignment/"]
RUN dotnet restore "WebCrawler Assignment/WebCrawler.csproj"

COPY . .
WORKDIR "/src/WebCrawler Assignment"
RUN dotnet publish "WebCrawler.csproj" \
	-c Release \
	-o /app/publish \
	--no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "WebCrawler.dll"]