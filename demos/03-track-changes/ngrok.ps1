cd .\demos\03-track-changes

# Run ngrok with the configured subdomain 
    ngrok http 5000 -subdomain=jvgraph

#start the listener ( or in debug mode) 
dotnet run

# trigger the app to create a subscription
invoke-webrequest "http://localhost:5000/api/notifications"
