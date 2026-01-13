import requests
import sys

# === CONFIG ===
jama_base_url = "https://jama02.rockwellcollins.com/contour"  
client_id = "YOUR_CLIENT_ID_HERE"  # Replace with your actual client ID
client_secret = "YOUR_CLIENT_SECRET_HERE"  # Replace with your actual client secret

# OAuth token endpoint
token_url = f"{jama_base_url}/rest/oauth/token"

# GET projects endpoint
projects_url = f"{jama_base_url}/rest/v1/projects"

print("=== Testing Jama OAuth Scope Issue ===")
print(f"Token URL: {token_url}")
print(f"Projects URL: {projects_url}")
print()

# === REQUEST TOKEN ===
print("Step 1: Requesting OAuth token...")
payload = {
    "grant_type": "client_credentials"
}

try:
    token_response = requests.post(
        token_url,
        data=payload,
        auth=(client_id, client_secret),
        verify=False  # Skip certificate verification for testing
    )
    
    if token_response.status_code != 200:
        print("❌ Failed to get token")
        print("Status Code:", token_response.status_code)
        print("Response:", token_response.text)
        sys.exit(1)
    
    token_info = token_response.json()
    access_token = token_info.get("access_token")
    print("✅ Bearer Token obtained successfully!")
    print(f"Token type: {token_info.get('token_type')}")
    print(f"Expires in: {token_info.get('expires_in')} seconds")
    print()

except Exception as e:
    print(f"❌ Error getting token: {e}")
    sys.exit(1)

# === CALL GET projects ===
print("Step 2: Testing projects API with OAuth token...")
headers = {
    "Authorization": f"Bearer {access_token}",
    "Accept": "application/json"
}

try:
    projects_response = requests.get(
        projects_url,
        headers=headers,
        verify=False  # Skip certificate verification for testing
    )
    
    if projects_response.status_code == 200:
        print("✅ Projects API call succeeded!")
        projects_data = projects_response.json()
        print(f"Found {len(projects_data.get('data', []))} projects")
        print("OAuth scope is correctly set to 'read'! 🎉")
    else:
        print("❌ Projects API call failed!")
        print("Status Code:", projects_response.status_code)
        print("Response:", projects_response.text)
        
        if projects_response.status_code == 500 and "IndexOutOfBounds" in projects_response.text:
            print()
            print("🎯 THIS IS THE OAUTH SCOPE ERROR!")
            print("The OAuth client scope is set to 'Token Information'")
            print("It needs to be changed to 'read' in Jama Admin Console")
            
except Exception as e:
    print(f"❌ Error calling projects API: {e}")