import React, { useState } from "react";

export const ConfigurationContext = React.createContext({
    config:{},
    setConfig: (config) => {},
    selectedShirtsColors: [],
    setSelectedShirtsColors: (selectedShirtsColors)=>{},
    getSelectedConfig: ()=>{},
    setAlgo: (algo) => {},
    algo: {}
});

export const ConfigurationContextProvider = (props) => {

    const [config, setConfig] = useState(null)
    const [algo, setAlgo] = useState(0)

    const [selectedShirtsColors, setSelectedShirtsColors] = useState(null)
    const [selectedEventDate, setSelectedEventDate] = useState(null)

    function getSelectedConfig() {
        return {
            numberOfTeams: config.config.numberOfTeams,
            shirtsColors: selectedShirtsColors,
            eventDate: config.config.eventDate
        }
    }

    function updateConfig(configParam){
        setConfig(configParam)
        setSelectedShirtsColors(configParam.config.shirtsColors.slice(0,3))
    }

    return <ConfigurationContext.Provider value={{
        setConfig: updateConfig,
        config: config,
        setSelectedShirtsColors: setSelectedShirtsColors,
        selectedShirtsColors: selectedShirtsColors,
        getSelectedConfig: getSelectedConfig,
        setAlgo,
        setSelectedEventDate:setSelectedEventDate,
        selectedEventDate:selectedEventDate,
        algo
        
    }}>
        {props.children}    
    </ConfigurationContext.Provider>
};

