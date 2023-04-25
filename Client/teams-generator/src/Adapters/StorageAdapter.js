export async function sendEvent(eventType, user, actionType, data) {
    console.log(eventType, user, actionType, data)
    if(data) {
        const response = await fetch(`${'https://teamsgeneratorapp-default-rtdb.firebaseio.com'}/${eventType}/${actionType}/${user}.json`, {
            method: 'post',
            body: JSON.stringify(data)
        });
    }
    else {
        const response = await fetch(`${'https://teamsgeneratorapp-default-rtdb.firebaseio.com'}/${eventType}/${actionType}/${user}.json`, {
            method: 'PUT',
            body: JSON.stringify(true)
        });
    }

    
}
