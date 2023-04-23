import React, { useEffect, useState } from "react";
import { v4 as uuidv4 } from 'uuid';

export const UserContext = React.createContext({
    user: {}
});

export const UserContextProvider = (props) => {
    
    const [user, setUser] = useState(null)

    useEffect(() => { 
        (async () => { 
            const userInStorage = localStorage.getItem('user')
            if (userInStorage) {
                setUser(JSON.parse(userInStorage))
            }
            else{
                const userGuid = uuidv4();
                const user = JSON.stringify({uid: userGuid})
                setUser(user)
                localStorage.setItem('user', user)
            }
        })() 
      },[])


    return <UserContext.Provider value={{
        user
    }}>
        {props.children}    
    </UserContext.Provider>
};

