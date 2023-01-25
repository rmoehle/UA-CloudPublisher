curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --version 6.0.405
echo 'export DOTNET_ROOT=$HOME/.dotnet' >> ~/.bashrc
echo 'export PATH=$PATH:$HOME/.dotnet' >> ~/.bashrc
source ~/.bashrc
dotnet restore "./UA-CloudPublisher.csproj"
dotnet build "UA-CloudPublisher.csproj" -c Release -o /app/build
dotnet publish "UA-CloudPublisher.csproj" -c Release -o /app/publish
cd /app/publish
dotnet UA-CloudPublisher.dll
